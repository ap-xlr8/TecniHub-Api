using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.Notifications;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Application.Reviews;

public sealed class PublicProfileService : IPublicProfileService
{
    private readonly IServiceReviewRepository _reviews;
    private readonly IRepairRequestRepository _repairs;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;
    private readonly INotificationService? _notifications;

    public PublicProfileService(
        IServiceReviewRepository reviews,
        IRepairRequestRepository repairs,
        IAuditLogger auditLogger,
        IDateTimeProvider clock,
        INotificationService? notifications = null)
    {
        _reviews = reviews;
        _repairs = repairs;
        _auditLogger = auditLogger;
        _clock = clock;
        _notifications = notifications;
    }

    public async Task<Result<PublicTechnicianProfileDto>> GetPublicTechnicianProfileAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default)
    {
        var technician = await _reviews.GetActiveUserWithProfileAsync(technicianUserId, cancellationToken);
        if (technician is null || technician.GetUserRole() != UserRole.Technician || technician.TechnicianProfile is null)
        {
            return Result<PublicTechnicianProfileDto>.Failure(new AppError(
                ErrorType.NotFound,
                "technician_profile_not_found",
                "Perfil público de técnico no disponible."));
        }

        var completedCount = await _reviews.CountCompletedServicesByTechnicianAsync(technician.Id, cancellationToken);
        var (reviewCount, averageRating) = await _reviews.GetReviewSummaryForTechnicianAsync(technician.Id, cancellationToken);
        var reviewEntities = await _reviews.GetReviewsForTechnicianAsync(technician.Id, cancellationToken);

        var mappedReviews = reviewEntities
            .Select(MapReview)
            .ToArray();

        return Result<PublicTechnicianProfileDto>.Success(new PublicTechnicianProfileDto(
            technician.Id,
            FormatDisplayName(technician),
            technician.TechnicianProfile.BusinessName,
            technician.TechnicianProfile.Specialties,
            technician.TechnicianProfile.ExperienceYears,
            technician.TechnicianProfile.ServiceZone,
            technician.CreatedAt,
            completedCount,
            reviewCount,
            averageRating,
            mappedReviews,
            technician.AvatarUrl));
    }

    public async Task<Result<PublicClientProfileDto>> GetPublicClientProfileAsync(
        Guid clientUserId,
        Guid actorUserId,
        UserRole actorRole,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (actorRole != UserRole.Technician)
        {
            await LogClientProfileDeniedAsync(actorUserId, actorRole.ToString(), clientUserId, metadata, cancellationToken);
            return Result<PublicClientProfileDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "client_profile_access_denied",
                "No tienes permiso para consultar el perfil público de este cliente."));
        }

        var hasRelationship = await _reviews.HasTechnicianWorkedWithClientAsync(actorUserId, clientUserId, cancellationToken);
        if (!hasRelationship)
        {
            await LogClientProfileDeniedAsync(actorUserId, actorRole.ToString(), clientUserId, metadata, cancellationToken);
            return Result<PublicClientProfileDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "client_profile_access_denied",
                "Solo los técnicos con una asignación confirmada pueden consultar el perfil público limitado del cliente."));
        }

        var client = await _reviews.GetActiveUserWithProfileAsync(clientUserId, cancellationToken);
        if (client is null || client.GetUserRole() != UserRole.Client)
        {
            return Result<PublicClientProfileDto>.Failure(new AppError(
                ErrorType.NotFound,
                "client_profile_not_found",
                "Perfil público de cliente no disponible."));
        }

        var completedCount = await _reviews.CountCompletedServicesByClientAsync(client.Id, cancellationToken);

        // Strictly limited public DTO: never exposes email, phone, street address, or client rating.
        return Result<PublicClientProfileDto>.Success(new PublicClientProfileDto(
            client.Id,
            FormatDisplayName(client),
            client.PublicRegion,
            client.CreatedAt,
            completedCount,
            client.AvatarUrl));
    }

    public async Task<Result<PublicReviewItemDto>> CreateReviewAsync(
        Guid repairRequestId,
        Guid clientUserId,
        CreateServiceReviewInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (input.Rating is < ServiceReview.MinRating or > ServiceReview.MaxRating)
        {
            errors.Add("La calificación debe ser un número entero entre 1 y 5.");
        }

        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (comment is not null && comment.Length > ServiceReview.MaxCommentLength)
        {
            errors.Add($"El comentario no debe superar {ServiceReview.MaxCommentLength} caracteres.");
        }

        if (errors.Count > 0)
        {
            return Result<PublicReviewItemDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos de tu reseña.",
                errors));
        }

        var repair = await _repairs.GetByIdAsync(repairRequestId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<PublicReviewItemDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.ClientUserId != clientUserId)
        {
            return Result<PublicReviewItemDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "review_not_allowed",
                "Solo el cliente propietario puede calificar este servicio."));
        }

        if (repair.Status != RepairRequestStatus.Completed || !repair.AssignedTechnicianId.HasValue)
        {
            return Result<PublicReviewItemDto>.Failure(new AppError(
                ErrorType.Conflict,
                "repair_not_completed",
                "Solo puedes dejar una reseña cuando la reparación haya sido confirmada como Completada."));
        }

        var existing = await _reviews.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        if (existing is not null)
        {
            return Result<PublicReviewItemDto>.Failure(new AppError(
                ErrorType.Conflict,
                "review_already_exists",
                "Este servicio ya cuenta con una reseña registrada."));
        }

        var utcNow = _clock.UtcNow;
        var review = new ServiceReview
        {
            Id = Guid.NewGuid(),
            RepairRequestId = repair.Id,
            TechnicianUserId = repair.AssignedTechnicianId.Value,
            ClientUserId = clientUserId,
            Rating = input.Rating,
            Comment = comment,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        var inserted = await _reviews.TryAddReviewAsync(review, cancellationToken);
        if (!inserted)
        {
            return Result<PublicReviewItemDto>.Failure(new AppError(
                ErrorType.Conflict,
                "review_already_exists",
                "Este servicio ya cuenta con una reseña registrada."));
        }

        await _auditLogger.LogAsync(
            action: "ServiceReviewCreated",
            entityName: "ServiceReview",
            entityId: review.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: clientUserId,
            actorRole: nameof(UserRole.Client),
            metadata: new { repairRequestId = repair.Id, technicianUserId = review.TechnicianUserId, rating = review.Rating },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        if (_notifications is not null)
        {
            await _notifications.NotifyAsync(
                review.TechnicianUserId,
                "ServiceReviewReceived",
                "Recibiste una nueva reseña",
                $"Un cliente calificó tu servicio de {repair.ApplianceName} con {review.Rating} / 5.",
                $"/perfiles/tecnico/{review.TechnicianUserId}",
                cancellationToken);
        }

        return Result<PublicReviewItemDto>.Success(new PublicReviewItemDto(
            review.Id,
            review.RepairRequestId,
            review.Rating,
            review.Comment,
            repair.ApplianceName,
            FormatDisplayName(repair.ClientUser),
            review.CreatedAt));
    }

    public async Task<Result<PublicReviewItemDto?>> GetReviewForRepairAsync(
        Guid repairRequestId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairRequestId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<PublicReviewItemDto?>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.ClientUserId != actorUserId && repair.AssignedTechnicianId != actorUserId)
        {
            return Result<PublicReviewItemDto?>.Failure(new AppError(
                ErrorType.Forbidden,
                "review_access_denied",
                "No tienes permiso para consultar la reseña de este servicio."));
        }

        var existing = await _reviews.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        return Result<PublicReviewItemDto?>.Success(existing is null ? null : MapReview(existing));
    }

    private Task LogClientProfileDeniedAsync(
        Guid actorUserId,
        string actorRole,
        Guid targetClientId,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        _auditLogger.LogAsync(
            action: "PublicClientProfileAccessDenied",
            entityName: "User",
            entityId: targetClientId.ToString(),
            outcome: AuditOutcome.Blocked,
            actorUserId: actorUserId,
            actorRole: actorRole,
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

    private static PublicReviewItemDto MapReview(ServiceReview review) => new(
        review.Id,
        review.RepairRequestId,
        review.Rating,
        review.Comment,
        review.RepairRequest?.ApplianceName ?? "Reparación",
        FormatDisplayName(review.ClientUser),
        review.CreatedAt);

    private static string FormatDisplayName(User? user)
    {
        if (user is null || user.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            return "Usuario eliminado";
        }

        return $"{user.FirstName} {user.LastName}".Trim();
    }
}
