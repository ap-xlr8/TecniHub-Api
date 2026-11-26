using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Domain.Rules;

namespace TECNIHUB.Application.Repairs;

public sealed class RepairRequestService : IRepairRequestService
{
    private const int OverlapWindowMinutes = 60;

    private readonly IRepairRequestRepository _repairs;
    private readonly IChatRepository _chat;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;

    public RepairRequestService(
        IRepairRequestRepository repairs,
        IChatRepository chat,
        IAuditLogger auditLogger,
        IDateTimeProvider clock)
    {
        _repairs = repairs;
        _chat = chat;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Result<RepairDetailDto>> CreateAsync(
        Guid clientUserId,
        CreateRepairRequestInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;
        var errors = ValidateCreateInput(input, utcNow);
        if (errors.Count > 0)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos de la solicitud de reparación.",
                errors));
        }

        var imageUrls = NormalizeImageUrls(input.ImageUrls);
        var repairId = Guid.NewGuid();
        var repair = new RepairRequest
        {
            Id = repairId,
            ClientUserId = clientUserId,
            ApplianceName = input.ApplianceName.Trim(),
            BrandModel = TrimOrNull(input.BrandModel),
            IssueDescription = input.IssueDescription.Trim(),
            PreferredDate = input.PreferredDate,
            PreferredTimeSlot = input.PreferredTimeSlot.Trim(),
            PostalCode = input.PostalCode.Trim(),
            State = input.State.Trim(),
            Municipality = input.Municipality.Trim(),
            City = TrimOrNull(input.City),
            Neighborhood = input.Neighborhood.Trim(),
            Street = input.Street.Trim(),
            ExteriorNumber = TrimOrNull(input.ExteriorNumber),
            InteriorNumber = TrimOrNull(input.InteriorNumber),
            AddressReference = TrimOrNull(input.AddressReference),
            Status = RepairRequestStatus.Open,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Images = imageUrls
                .Select((url, index) => new RepairRequestImage
                {
                    Id = Guid.NewGuid(),
                    RepairRequestId = repairId,
                    ImageUrl = url,
                    DisplayOrder = index,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                })
                .ToList()
        };

        await _repairs.AddAsync(repair, cancellationToken);
        await _repairs.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "RepairRequestCreated",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: clientUserId,
            actorRole: nameof(UserRole.Client),
            metadata: new { imageCount = repair.Images.Count, postalCode = repair.PostalCode },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var loaded = await _repairs.GetByIdAsync(repair.Id, cancellationToken) ?? repair;
        return Result<RepairDetailDto>.Success(MapDetail(loaded, clientUserId, conversationId: null));
    }

    public async Task<Result<IReadOnlyList<ClientRepairSummaryDto>>> GetClientRequestsAsync(
        Guid clientUserId,
        CancellationToken cancellationToken = default)
    {
        var items = await _repairs.GetByClientAsync(clientUserId, cancellationToken);
        var mapped = items.Select(r => new ClientRepairSummaryDto(
            r.Id,
            r.ApplianceName,
            r.BrandModel,
            Summarize(r.IssueDescription, 140),
            r.PreferredDate,
            r.PreferredTimeSlot,
            MapPublicLocation(r),
            r.Status.ToString(),
            GetStatusLabel(r.Status),
            FormatDisplayName(r.AssignedTechnician),
            r.ScheduledStartAt,
            r.ClosureRequestedAt,
            r.CompletedAt,
            r.Images.Count,
            r.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
            r.CreatedAt)).ToArray();

        return Result<IReadOnlyList<ClientRepairSummaryDto>>.Success(mapped);
    }

    public async Task<Result<IReadOnlyList<OpenRepairBoardItemDto>>> GetOpenBoardAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var items = await _repairs.GetOpenBoardAsync(search, cancellationToken);
        // Never project street/numbers or client PII onto the open board DTO.
        var mapped = items.Select(r => new OpenRepairBoardItemDto(
            r.Id,
            r.ApplianceName,
            r.BrandModel,
            Summarize(r.IssueDescription, 160),
            r.PreferredDate,
            r.PreferredTimeSlot,
            MapPublicLocation(r),
            r.Status.ToString(),
            GetStatusLabel(r.Status),
            r.Images.Count,
            r.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
            r.CreatedAt)).ToArray();

        return Result<IReadOnlyList<OpenRepairBoardItemDto>>.Success(mapped);
    }

    public async Task<Result<IReadOnlyList<ItineraryItemDto>>> GetTechnicianItineraryAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default)
    {
        var items = await _repairs.GetByAssignedTechnicianAsync(technicianUserId, cancellationToken);
        var activeScheduled = items
            .Where(r => r.ScheduledStartAt.HasValue
                && r.Status is RepairRequestStatus.Assigned
                    or RepairRequestStatus.Scheduled
                    or RepairRequestStatus.InProgress
                    or RepairRequestStatus.ClosureRequested)
            .ToList();

        var ordered = items
            .Where(r => r.IsConfirmedAssignment)
            .OrderBy(r => r.Status is RepairRequestStatus.Completed or RepairRequestStatus.Cancelled ? 2 : r.ScheduledStartAt.HasValue ? 0 : 1)
            .ThenBy(r => r.ScheduledStartAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(r => r.AssignedAt ?? r.CreatedAt)
            .Select(r =>
            {
                var hasOverlap = r.ScheduledStartAt.HasValue
                    && r.Status is not (RepairRequestStatus.Completed or RepairRequestStatus.Cancelled)
                    && activeScheduled.Any(other =>
                        other.Id != r.Id
                        && other.ScheduledStartAt.HasValue
                        && Math.Abs((other.ScheduledStartAt.Value - r.ScheduledStartAt.Value).TotalMinutes) < OverlapWindowMinutes);

                return new ItineraryItemDto(
                    r.Id,
                    r.ApplianceName,
                    r.BrandModel,
                    FormatDisplayName(r.ClientUser) ?? "Cliente",
                    MapPublicLocation(r),
                    new FullAddressDto(r.Street, r.ExteriorNumber, r.InteriorNumber, r.AddressReference),
                    r.PreferredDate,
                    r.PreferredTimeSlot,
                    r.ScheduledStartAt,
                    r.Status.ToString(),
                    GetStatusLabel(r.Status),
                    hasOverlap,
                    r.AssignedAt ?? r.CreatedAt);
            })
            .ToArray();

        return Result<IReadOnlyList<ItineraryItemDto>>.Success(ordered);
    }

    public async Task<Result<RepairDetailDto>> GetDetailAsync(
        Guid repairId,
        Guid actorUserId,
        UserRole actorRole,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        var isOwner = repair.ClientUserId == actorUserId;
        var isAssignedTech = repair.AssignedTechnicianId == actorUserId;
        var isTechnicianViewingOpen = actorRole == UserRole.Technician && repair.Status == RepairRequestStatus.Open;

        if (!isOwner && !isAssignedTech && !isTechnicianViewingOpen)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "repair_access_denied",
                "No tienes permiso para consultar esta solicitud."));
        }

        Guid? conversationId = null;
        if (repair.IsConfirmedAssignment && (isOwner || isAssignedTech))
        {
            var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
            conversationId = conversation?.Id;
        }

        return Result<RepairDetailDto>.Success(MapDetail(repair, actorUserId, conversationId));
    }

    public async Task<Result<RepairDetailDto>> UpdateImagesAsync(
        Guid repairId,
        Guid clientUserId,
        UpdateRepairImagesInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.ClientUserId != clientUserId)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "repair_not_owned",
                "Solo el cliente propietario puede actualizar las imágenes de esta solicitud."));
        }

        if (repair.Status is RepairRequestStatus.Completed or RepairRequestStatus.Cancelled)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "repair_closed",
                "No se pueden modificar las imágenes de una solicitud cerrada o cancelada."));
        }

        var errors = ValidateImageUrls(input.ImageUrls);
        if (errors.Count > 0)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_images",
                "Revisa las imágenes seleccionadas.",
                errors));
        }

        var utcNow = _clock.UtcNow;
        var urls = NormalizeImageUrls(input.ImageUrls);
        var newImages = urls.Select((url, idx) => new RepairRequestImage
        {
            Id = Guid.NewGuid(),
            RepairRequestId = repair.Id,
            ImageUrl = url,
            DisplayOrder = idx,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        }).ToArray();

        await _repairs.ReplaceImagesAsync(repair.Id, newImages, utcNow, cancellationToken);
        await _repairs.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "RepairImagesUpdated",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: clientUserId,
            actorRole: nameof(UserRole.Client),
            metadata: new { imageCount = newImages.Length },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var updated = await _repairs.GetByIdAsync(repair.Id, cancellationToken) ?? repair;
        var conversation = updated.IsConfirmedAssignment
            ? await _chat.GetByRepairRequestIdAsync(updated.Id, cancellationToken)
            : null;
        return Result<RepairDetailDto>.Success(MapDetail(updated, clientUserId, conversation?.Id));
    }

    public async Task<Result<RepairDetailDto>> AcceptAsync(
        Guid repairId,
        Guid technicianUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _clock.UtcNow;
        var accepted = await _repairs.TryAcceptAtomicAsync(repairId, technicianUserId, utcNow, cancellationToken);
        if (!accepted)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "repair_not_available",
                "La solicitud ya no está disponible para aceptación."));
        }

        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud no encontrada tras la asignación."));
        }

        var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        if (conversation is null)
        {
            conversation = new ChatConversation
            {
                Id = Guid.NewGuid(),
                ContextType = ConversationContextType.RepairRequest,
                RepairRequestId = repair.Id,
                ParticipantAUserId = repair.ClientUserId,
                ParticipantBUserId = technicianUserId,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };
            await _chat.AddConversationAsync(conversation, cancellationToken);
            await _chat.SaveChangesAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(
            action: "RepairRequestAccepted",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            metadata: new { conversationId = conversation.Id },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<RepairDetailDto>.Success(MapDetail(repair, technicianUserId, conversation.Id));
    }

    public async Task<Result<ScheduleRepairResponse>> ScheduleAsync(
        Guid repairId,
        Guid technicianUserId,
        ScheduleRepairInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<ScheduleRepairResponse>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.AssignedTechnicianId != technicianUserId)
        {
            return Result<ScheduleRepairResponse>.Failure(new AppError(
                ErrorType.Forbidden,
                "repair_not_assigned_to_technician",
                "Solo el técnico asignado puede programar este servicio en su itinerario."));
        }

        var utcNow = _clock.UtcNow;
        var scheduledUtc = input.ScheduledStartAt.ToUniversalTime();
        if (scheduledUtc < utcNow.AddMinutes(-5))
        {
            return Result<ScheduleRepairResponse>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_schedule_time",
                "La fecha y hora programada debe ser actual o futura."));
        }

        if (!repair.Schedule(technicianUserId, scheduledUtc, utcNow))
        {
            return Result<ScheduleRepairResponse>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo se pueden programar servicios en estado Asignada o Programada."));
        }

        await _repairs.SaveChangesAsync(cancellationToken);

        var technicianRepairs = await _repairs.GetByAssignedTechnicianAsync(technicianUserId, cancellationToken);
        var overlaps = technicianRepairs
            .Where(other => other.Id != repair.Id
                && other.ScheduledStartAt.HasValue
                && other.Status is not (RepairRequestStatus.Completed or RepairRequestStatus.Cancelled)
                && Math.Abs((other.ScheduledStartAt.Value - scheduledUtc).TotalMinutes) < OverlapWindowMinutes)
            .OrderBy(other => other.ScheduledStartAt)
            .Select(other => new OverlappingAppointmentDto(other.Id, other.ApplianceName, other.ScheduledStartAt!.Value))
            .ToArray();

        await _auditLogger.LogAsync(
            action: "RepairRequestScheduled",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            metadata: new { scheduledStartAt = scheduledUtc, hasOverlap = overlaps.Length > 0 },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        return Result<ScheduleRepairResponse>.Success(new ScheduleRepairResponse(
            MapDetail(repair, technicianUserId, conversation?.Id),
            overlaps.Length > 0,
            overlaps));
    }

    public async Task<Result<RepairDetailDto>> StartWorkAsync(
        Guid repairId,
        Guid technicianUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.AssignedTechnicianId != technicianUserId)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "repair_not_assigned_to_technician",
                "Solo el técnico asignado puede iniciar esta reparación."));
        }

        var utcNow = _clock.UtcNow;
        if (!repair.StartWork(technicianUserId, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "El servicio debe estar en estado Asignada o Programada para iniciar la reparación."));
        }

        await _repairs.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "RepairRequestStarted",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        return Result<RepairDetailDto>.Success(MapDetail(repair, technicianUserId, conversation?.Id));
    }

    public async Task<Result<RepairDetailDto>> RequestClosureAsync(
        Guid repairId,
        Guid technicianUserId,
        RequestClosureInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.AssignedTechnicianId != technicianUserId)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "repair_not_assigned_to_technician",
                "Solo el técnico asignado puede solicitar el cierre del servicio."));
        }

        if (!string.IsNullOrWhiteSpace(input.ClosureNotes) && input.ClosureNotes.Trim().Length > 500)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Las notas de cierre no deben superar 500 caracteres."));
        }

        var utcNow = _clock.UtcNow;
        if (!repair.RequestClosure(technicianUserId, input.ClosureNotes, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo puedes solicitar cierre cuando el servicio está En proceso."));
        }

        await _repairs.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "RepairClosureRequested",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        return Result<RepairDetailDto>.Success(MapDetail(repair, technicianUserId, conversation?.Id));
    }

    public async Task<Result<RepairDetailDto>> ConfirmClosureAsync(
        Guid repairId,
        Guid clientUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.ClientUserId != clientUserId)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "repair_not_owned",
                "Solo el cliente propietario puede confirmar la finalización del servicio."));
        }

        var utcNow = _clock.UtcNow;
        if (!repair.ConfirmClosure(clientUserId, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "El cierre solo puede confirmarse cuando el técnico ya lo solicitó."));
        }

        await _repairs.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "RepairClosureConfirmed",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: clientUserId,
            actorRole: nameof(UserRole.Client),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        return Result<RepairDetailDto>.Success(MapDetail(repair, clientUserId, conversation?.Id));
    }

    public async Task<Result<RepairDetailDto>> CancelAsync(
        Guid repairId,
        Guid clientUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        if (repair.ClientUserId != clientUserId)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "repair_not_owned",
                "Solo el cliente propietario puede cancelar esta solicitud."));
        }

        var utcNow = _clock.UtcNow;
        var cancelled = await _repairs.TryCancelAtomicAsync(repair.Id, clientUserId, utcNow, cancellationToken);
        if (!cancelled)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo es posible cancelar una solicitud mientras siga disponible y sin técnico asignado."));
        }

        await _auditLogger.LogAsync(
            action: "RepairRequestCancelled",
            entityName: "RepairRequest",
            entityId: repair.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: clientUserId,
            actorRole: nameof(UserRole.Client),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var updated = await _repairs.GetByIdAsync(repair.Id, cancellationToken) ?? repair;
        return Result<RepairDetailDto>.Success(MapDetail(updated, clientUserId, conversationId: null));
    }

    public static string GetStatusLabel(RepairRequestStatus status) => status switch
    {
        RepairRequestStatus.Open => "Disponible",
        RepairRequestStatus.Assigned => "Asignada",
        RepairRequestStatus.Scheduled => "Programada",
        RepairRequestStatus.InProgress => "En proceso",
        RepairRequestStatus.ClosureRequested => "Cierre solicitado",
        RepairRequestStatus.Completed => "Completado",
        RepairRequestStatus.Cancelled => "Cancelada",
        _ => "Desconocido"
    };

    private static List<string> ValidateCreateInput(CreateRepairRequestInput input, DateTimeOffset utcNow)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.ApplianceName) || input.ApplianceName.Trim().Length > 120)
        {
            errors.Add("El nombre o tipo de electrodoméstico es obligatorio (máximo 120 caracteres).");
        }

        if (!string.IsNullOrWhiteSpace(input.BrandModel) && input.BrandModel.Trim().Length > 120)
        {
            errors.Add("La marca o modelo no debe superar 120 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(input.IssueDescription) || input.IssueDescription.Trim().Length < 10 || input.IssueDescription.Trim().Length > 1000)
        {
            errors.Add("Describe la falla con al menos 10 caracteres (máximo 1000).");
        }

        var earliestAllowed = DateOnly.FromDateTime(utcNow.UtcDateTime.AddDays(-1));
        if (input.PreferredDate < earliestAllowed)
        {
            errors.Add("La fecha deseada no puede ser anterior a hoy.");
        }

        if (string.IsNullOrWhiteSpace(input.PreferredTimeSlot) || input.PreferredTimeSlot.Trim().Length > 80)
        {
            errors.Add("El horario deseado es obligatorio (máximo 80 caracteres).");
        }

        var cp = input.PostalCode?.Trim() ?? string.Empty;
        if (cp.Length != 5 || !cp.All(char.IsDigit))
        {
            errors.Add("El código postal debe tener exactamente 5 dígitos.");
        }

        if (string.IsNullOrWhiteSpace(input.State) || input.State.Trim().Length > 80)
        {
            errors.Add("El estado es obligatorio (máximo 80 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(input.Municipality) || input.Municipality.Trim().Length > 100)
        {
            errors.Add("El municipio o alcaldía es obligatorio (máximo 100 caracteres).");
        }

        if (!string.IsNullOrWhiteSpace(input.City) && input.City.Trim().Length > 100)
        {
            errors.Add("La ciudad no debe superar 100 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(input.Neighborhood) || input.Neighborhood.Trim().Length > 120)
        {
            errors.Add("La colonia es obligatoria (máximo 120 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(input.Street) || input.Street.Trim().Length > 150)
        {
            errors.Add("La calle es obligatoria (máximo 150 caracteres).");
        }

        if (!string.IsNullOrWhiteSpace(input.ExteriorNumber) && input.ExteriorNumber.Trim().Length > 30)
        {
            errors.Add("El número exterior no debe superar 30 caracteres.");
        }

        if (!string.IsNullOrWhiteSpace(input.InteriorNumber) && input.InteriorNumber.Trim().Length > 30)
        {
            errors.Add("El número interior no debe superar 30 caracteres.");
        }

        if (!string.IsNullOrWhiteSpace(input.AddressReference) && input.AddressReference.Trim().Length > 250)
        {
            errors.Add("Las referencias del domicilio no deben superar 250 caracteres.");
        }

        errors.AddRange(ValidateImageUrls(input.ImageUrls));
        return errors;
    }

    private static List<string> ValidateImageUrls(IReadOnlyList<string>? imageUrls)
    {
        var errors = new List<string>();
        if (imageUrls is null || imageUrls.Count == 0)
        {
            return errors;
        }

        if (imageUrls.Count > RepairRequest.MaxImages)
        {
            errors.Add($"Solo se permiten hasta {RepairRequest.MaxImages} imágenes por solicitud.");
            return errors;
        }

        foreach (var url in imageUrls)
        {
            if (!ImageSignatureValidator.IsAllowedHostedImageUrl(url))
            {
                errors.Add("Todas las imágenes deben haberse cargado correctamente en el servicio de almacenamiento permitido.");
                break;
            }
        }

        return errors;
    }

    private static string[] NormalizeImageUrls(IReadOnlyList<string>? imageUrls) =>
        imageUrls is null
            ? []
            : imageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToArray();

    private static PublicLocationDto MapPublicLocation(RepairRequest repair) => new(
        repair.PostalCode,
        repair.State,
        repair.Municipality,
        repair.City,
        repair.Neighborhood);

    private static RepairDetailDto MapDetail(RepairRequest repair, Guid actorUserId, Guid? conversationId)
    {
        var canSeeFullAddress = repair.CanExposeFullAddressTo(actorUserId);
        return new RepairDetailDto(
            repair.Id,
            canSeeFullAddress ? repair.ClientUserId : Guid.Empty,
            canSeeFullAddress ? FormatDisplayName(repair.ClientUser) ?? "Cliente" : "Cliente",
            repair.AssignedTechnicianId,
            FormatDisplayName(repair.AssignedTechnician),
            repair.ApplianceName,
            repair.BrandModel,
            repair.IssueDescription,
            repair.PreferredDate,
            repair.PreferredTimeSlot,
            MapPublicLocation(repair),
            canSeeFullAddress
                ? new FullAddressDto(repair.Street, repair.ExteriorNumber, repair.InteriorNumber, repair.AddressReference)
                : null,
            repair.Status.ToString(),
            GetStatusLabel(repair.Status),
            repair.AssignedAt,
            repair.ScheduledStartAt,
            repair.StartedAt,
            repair.ClosureRequestedAt,
            repair.ClosureNotes,
            repair.CompletedAt,
            repair.CancelledAt,
            repair.CreatedAt,
            repair.UpdatedAt,
            repair.Images.OrderBy(i => i.DisplayOrder).Select(i => new ImageItemDto(i.Id, i.ImageUrl, i.DisplayOrder)).ToArray(),
            conversationId);
    }

    private static string? FormatDisplayName(User? user)
    {
        if (user is null)
        {
            return null;
        }

        if (user.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            return "Usuario eliminado";
        }

        return $"{user.FirstName} {user.LastName}".Trim();
    }

    private static string Summarize(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..(maxLength - 1)].TrimEnd()}…";

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
