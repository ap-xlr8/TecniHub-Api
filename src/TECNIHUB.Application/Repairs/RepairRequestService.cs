using System.Security.Cryptography;
using System.Text;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.Notifications;
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
    private readonly INotificationService? _notifications;

    public RepairRequestService(
        IRepairRequestRepository repairs,
        IChatRepository chat,
        IAuditLogger auditLogger,
        IDateTimeProvider clock,
        INotificationService? notifications = null)
    {
        _repairs = repairs;
        _chat = chat;
        _auditLogger = auditLogger;
        _clock = clock;
        _notifications = notifications;
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
        var mapped = items.Select(r =>
        {
            var orderedUrls = r.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).ToArray();
            return new ClientRepairSummaryDto(
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
                orderedUrls.Length,
                orderedUrls.FirstOrDefault(),
                r.CreatedAt,
                orderedUrls);
        }).ToArray();

        return Result<IReadOnlyList<ClientRepairSummaryDto>>.Success(mapped);
    }

    public async Task<Result<IReadOnlyList<OpenRepairBoardItemDto>>> GetOpenBoardAsync(
        string? search,
        string? state = null,
        string? municipality = null,
        string? postalCode = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _repairs.GetOpenBoardAsync(search, state, municipality, postalCode, cancellationToken);
        // Never project street/numbers or client PII onto the open board DTO.
        var mapped = items.Select(r =>
        {
            var orderedUrls = r.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).ToArray();
            return new OpenRepairBoardItemDto(
                r.Id,
                r.ApplianceName,
                r.BrandModel,
                Summarize(r.IssueDescription, 160),
                r.PreferredDate,
                r.PreferredTimeSlot,
                MapPublicLocation(r),
                r.Status.ToString(),
                GetStatusLabel(r.Status),
                orderedUrls.Length,
                orderedUrls.FirstOrDefault(),
                r.CreatedAt,
                orderedUrls);
        }).ToArray();

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
                    or RepairRequestStatus.OnTheWay
                    or RepairRequestStatus.Arrived
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

                var orderedImages = r.Images
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.ImageUrl)
                    .ToArray();

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
                    r.AssignedAt ?? r.CreatedAt,
                    orderedImages.Length,
                    orderedImages.FirstOrDefault(),
                    orderedImages);
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
        if (repair.IsConfirmedAssignment && repair.AssignedTechnicianId.HasValue && (isOwner || isAssignedTech))
        {
            var conversation = await _chat.GetByRepairRequestAndTechnicianAsync(repair.Id, repair.AssignedTechnicianId.Value, cancellationToken);
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

        var conversation = await _chat.GetByRepairRequestAndTechnicianAsync(repair.Id, technicianUserId, cancellationToken);
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

        await NotifySafeAsync(
            repair.ClientUserId,
            "RepairAccepted",
            "Un técnico aceptó tu solicitud",
            $"{FormatDisplayName(repair.AssignedTechnician) ?? "Un técnico"} aceptó atender tu {repair.ApplianceName}. Ya puedes chatear y revisar su perfil.",
            $"/cliente/solicitudes/{repair.Id}",
            cancellationToken);

        return Result<RepairDetailDto>.Success(MapDetail(repair, technicianUserId, conversation.Id));
    }

    public async Task<Result<ScheduleRepairResponse>> ScheduleAsync(
        Guid repairId,
        Guid technicianUserId,
        ScheduleRepairInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var (repair, loadError) = await LoadForAssignedTechnicianAsync(
            repairId,
            technicianUserId,
            "Solo el técnico asignado puede programar este servicio en su itinerario.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<ScheduleRepairResponse>.Failure(loadError);
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

        if (!repair!.Schedule(technicianUserId, scheduledUtc, utcNow))
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

        await LogAuditAsync(
            repair.Id,
            new TransitionContext(technicianUserId, UserRole.Technician, "RepairRequestScheduled", metadata, new { scheduledStartAt = scheduledUtc, hasOverlap = overlaps.Length > 0 }),
            cancellationToken: cancellationToken);

        await NotifySafeAsync(
            repair.ClientUserId,
            "RepairScheduled",
            "Cita programada",
            $"Tu técnico programó la visita para tu {repair.ApplianceName}.",
            $"/cliente/solicitudes/{repair.Id}",
            cancellationToken);

        var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
        return Result<ScheduleRepairResponse>.Success(new ScheduleRepairResponse(
            MapDetail(repair, technicianUserId, conversation?.Id),
            overlaps.Length > 0,
            overlaps));
    }

    public async Task<Result<RepairDetailDto>> MarkOnTheWayAsync(
        Guid repairId,
        Guid technicianUserId,
        MarkOnTheWayInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (input.EstimatedArrivalMinutes is < 5 or > 180)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_eta",
                "El tiempo estimado de llegada debe estar entre 5 y 180 minutos."));
        }

        var (repair, loadError) = await LoadForAssignedTechnicianAsync(
            repairId,
            technicianUserId,
            "Solo el técnico asignado puede actualizar el estado de traslado.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        var utcNow = _clock.UtcNow;
        var pin = GenerateArrivalPin();
        if (!repair!.MarkOnTheWay(technicianUserId, input.EstimatedArrivalMinutes, pin, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo puedes marcar En camino cuando la solicitud está Asignada o Programada."));
        }

        var etaText = repair.EstimatedArrivalMinutes.HasValue
            ? $" Tiempo estimado: ~{repair.EstimatedArrivalMinutes} min."
            : string.Empty;

        // Never log the arrival PIN in audit_logs.
        return await SaveAuditNotifyAndMapAsync(
            repair,
            new TransitionContext(technicianUserId, UserRole.Technician, "RepairTechnicianOnTheWay", metadata, new { estimatedArrivalMinutes = repair.EstimatedArrivalMinutes }),
            new NotificationSpec(
                repair.ClientUserId,
                "RepairTechnicianOnTheWay",
                "Tu técnico va en camino",
                $"El técnico salió rumbo a tu domicilio para atender tu {repair.ApplianceName}.{etaText} Revisa tu código de llegada en el detalle.",
                $"/cliente/solicitudes/{repair.Id}"),
            includeConversation: true,
            cancellationToken);
    }

    public async Task<Result<RepairDetailDto>> MarkArrivedAsync(
        Guid repairId,
        Guid technicianUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var (repair, loadError) = await LoadForAssignedTechnicianAsync(
            repairId,
            technicianUserId,
            "Solo el técnico asignado puede confirmar su llegada.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        var utcNow = _clock.UtcNow;
        var pin = GenerateArrivalPin();
        if (!repair!.MarkArrived(technicianUserId, pin, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo puedes marcar llegada desde Asignada, Programada o En camino."));
        }

        return await SaveAuditNotifyAndMapAsync(
            repair,
            new TransitionContext(technicianUserId, UserRole.Technician, "RepairTechnicianArrived", metadata),
            new NotificationSpec(
                repair.ClientUserId,
                "RepairTechnicianArrived",
                "Tu técnico llegó al domicilio",
                $"El técnico ya se encuentra en tu domicilio para revisar tu {repair.ApplianceName}. Compártele tu código PIN de 4 dígitos para iniciar.",
                $"/cliente/solicitudes/{repair.Id}"),
            includeConversation: true,
            cancellationToken);
    }

    public async Task<Result<RepairDetailDto>> StartWorkAsync(
        Guid repairId,
        Guid technicianUserId,
        RequestMetadata metadata,
        StartWorkInput? input = null,
        CancellationToken cancellationToken = default)
    {
        var (repair, loadError) = await LoadForAssignedTechnicianAsync(
            repairId,
            technicianUserId,
            "Solo el técnico asignado puede iniciar esta reparación.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        if (repair!.Status is not (RepairRequestStatus.OnTheWay or RepairRequestStatus.Arrived)
            || string.IsNullOrEmpty(repair.ArrivalPinCode))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Debes marcar En camino o En el domicilio y verificar el código PIN del cliente antes de iniciar la reparación."));
        }

        var utcNow = _clock.UtcNow;
        var supplied = input?.ArrivalPinCode?.Trim() ?? string.Empty;
        if (!PinMatchesConstantTime(repair.ArrivalPinCode, supplied))
        {
            repair.ArrivalPinFailedAttempts++;
            var rotated = false;
            if (repair.ArrivalPinFailedAttempts >= 5)
            {
                repair.RotateArrivalPin(GenerateArrivalPin(), utcNow);
                rotated = true;
            }
            else
            {
                repair.UpdatedAt = utcNow;
            }

            await _repairs.SaveChangesAsync(cancellationToken);

            await LogAuditAsync(
                repair.Id,
                new TransitionContext(technicianUserId, UserRole.Technician, "RepairArrivalPinInvalid", metadata, new { failedAttempts = repair.ArrivalPinFailedAttempts, pinRotated = rotated }),
                AuditOutcome.Failure,
                cancellationToken);

            if (rotated)
            {
                await NotifySafeAsync(
                    repair.ClientUserId,
                    "RepairArrivalPinRotated",
                    "Nuevo código de llegada generado",
                    $"Por seguridad se generó un nuevo código PIN de llegada para tu {repair.ApplianceName}. Consúltalo en el detalle.",
                    $"/cliente/solicitudes/{repair.Id}",
                    cancellationToken);
            }

            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_arrival_pin",
                rotated
                    ? "Demasiados intentos fallidos. Por seguridad se generó un nuevo código PIN para el cliente."
                    : "Ingresa el código PIN de 4 dígitos proporcionado por el cliente en el domicilio para iniciar la reparación."));
        }

        if (!repair.StartWork(technicianUserId, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "El servicio debe estar En camino o En el domicilio para iniciar la reparación."));
        }

        return await SaveAuditNotifyAndMapAsync(
            repair,
            new TransitionContext(technicianUserId, UserRole.Technician, "RepairRequestStarted", metadata),
            new NotificationSpec(
                repair.ClientUserId,
                "RepairStarted",
                "Reparación iniciada",
                $"Tu reparación de {repair.ApplianceName} ya está En proceso.",
                $"/cliente/solicitudes/{repair.Id}"),
            includeConversation: true,
            cancellationToken);
    }

    public async Task<Result<RepairDetailDto>> RequestClosureAsync(
        Guid repairId,
        Guid technicianUserId,
        RequestClosureInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var (repair, loadError) = await LoadForAssignedTechnicianAsync(
            repairId,
            technicianUserId,
            "Solo el técnico asignado puede solicitar el cierre del servicio.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        if (!string.IsNullOrWhiteSpace(input.ClosureNotes) && input.ClosureNotes.Trim().Length > 500)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Las notas de cierre no deben superar 500 caracteres."));
        }

        var utcNow = _clock.UtcNow;
        if (!repair!.RequestClosure(technicianUserId, input.ClosureNotes, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo puedes solicitar cierre cuando el servicio está En proceso."));
        }

        return await SaveAuditNotifyAndMapAsync(
            repair,
            new TransitionContext(technicianUserId, UserRole.Technician, "RepairClosureRequested", metadata),
            new NotificationSpec(
                repair.ClientUserId,
                "RepairClosureRequested",
                "Confirmación de cierre pendiente",
                $"El técnico solicitó cerrar tu servicio de {repair.ApplianceName}. Revísalo y confirma o reporta cualquier pendiente.",
                $"/cliente/solicitudes/{repair.Id}"),
            includeConversation: true,
            cancellationToken);
    }

    public async Task<Result<RepairDetailDto>> RejectClosureAsync(
        Guid repairId,
        Guid clientUserId,
        RejectClosureInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 5 || reason.Length > 500)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Indica el motivo o pendiente detectado (entre 5 y 500 caracteres)."));
        }

        var (repair, loadError) = await LoadForOwnerClientAsync(
            repairId,
            clientUserId,
            "Solo el cliente propietario puede devolver el cierre del servicio.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        var utcNow = _clock.UtcNow;
        if (!repair!.RejectClosure(clientUserId, reason, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo puedes reportar un pendiente cuando el técnico ya solicitó el cierre."));
        }

        return await SaveAuditNotifyAndMapAsync(
            repair,
            new TransitionContext(clientUserId, UserRole.Client, "RepairClosureRejected", metadata),
            new NotificationSpec(
                repair.AssignedTechnicianId,
                "RepairClosureRejected",
                "El cliente reportó un pendiente",
                $"El servicio de {repair.ApplianceName} regresó a En proceso. Revisa el comentario del cliente.",
                $"/tecnico/servicios/{repair.Id}"),
            includeConversation: true,
            cancellationToken);
    }

    public async Task<Result<RepairDetailDto>> ConfirmClosureAsync(
        Guid repairId,
        Guid clientUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var (repair, loadError) = await LoadForOwnerClientAsync(
            repairId,
            clientUserId,
            "Solo el cliente propietario puede confirmar la finalización del servicio.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        var utcNow = _clock.UtcNow;
        if (!repair!.ConfirmClosure(clientUserId, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "El cierre solo puede confirmarse cuando el técnico ya lo solicitó."));
        }

        return await SaveAuditNotifyAndMapAsync(
            repair,
            new TransitionContext(clientUserId, UserRole.Client, "RepairClosureConfirmed", metadata),
            new NotificationSpec(
                repair.AssignedTechnicianId,
                "RepairCompleted",
                "Servicio confirmado como Completado",
                $"El cliente confirmó la finalización del servicio de {repair.ApplianceName}.",
                $"/tecnico/servicios/{repair.Id}"),
            includeConversation: true,
            cancellationToken);
    }

    public async Task<Result<RepairDetailDto>> ReleaseByTechnicianAsync(
        Guid repairId,
        Guid technicianUserId,
        ReleaseRepairInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 5 || reason.Length > 300)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Indica el motivo por el que liberas la asignación (entre 5 y 300 caracteres)."));
        }

        var (repair, loadError) = await LoadForAssignedTechnicianAsync(
            repairId,
            technicianUserId,
            "Solo el técnico asignado puede liberar esta solicitud.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        var utcNow = _clock.UtcNow;
        if (!repair!.ReleaseByTechnician(technicianUserId, utcNow))
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Conflict,
                "invalid_state_transition",
                "Solo puedes liberar una solicitud antes de llegar al domicilio o iniciar la reparación."));
        }

        return await SaveAuditNotifyAndMapAsync(
            repair,
            new TransitionContext(technicianUserId, UserRole.Technician, "RepairAssignmentReleased", metadata, new { reasonLength = reason.Length }),
            new NotificationSpec(
                repair.ClientUserId,
                "RepairReleased",
                "Tu solicitud volvió a estar disponible",
                $"El técnico liberó tu solicitud de {repair.ApplianceName} ({reason}). Ya está nuevamente visible en el tablón para otro técnico.",
                $"/cliente/solicitudes/{repair.Id}"),
            includeConversation: false,
            cancellationToken);
    }

    public async Task<Result<RepairDetailDto>> CancelAsync(
        Guid repairId,
        Guid clientUserId,
        RequestMetadata metadata,
        CancelRepairInput? input = null,
        CancellationToken cancellationToken = default)
    {
        var reason = TrimOrNull(input?.Reason);
        if (reason is not null && reason.Length > 300)
        {
            return Result<RepairDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "El motivo de cancelación no debe superar 300 caracteres."));
        }

        var (repair, loadError) = await LoadForOwnerClientAsync(
            repairId,
            clientUserId,
            "Solo el cliente propietario puede cancelar esta solicitud.",
            cancellationToken);
        if (loadError is not null)
        {
            return Result<RepairDetailDto>.Failure(loadError);
        }

        var utcNow = _clock.UtcNow;
        var previousTechnicianId = repair!.AssignedTechnicianId;

        if (repair.Status == RepairRequestStatus.Open && !previousTechnicianId.HasValue && reason is null)
        {
            var cancelled = await _repairs.TryCancelAtomicAsync(repair.Id, clientUserId, utcNow, cancellationToken);
            if (!cancelled)
            {
                return Result<RepairDetailDto>.Failure(new AppError(
                    ErrorType.Conflict,
                    "invalid_state_transition",
                    "No fue posible cancelar la solicitud en su estado actual."));
            }
        }
        else
        {
            if (!repair.Cancel(utcNow, reason))
            {
                return Result<RepairDetailDto>.Failure(new AppError(
                    ErrorType.Conflict,
                    "invalid_state_transition",
                    "Solo es posible cancelar una solicitud antes de que el técnico salga hacia tu domicilio."));
            }

            await _repairs.SaveChangesAsync(cancellationToken);
        }

        await LogAuditAsync(
            repair.Id,
            new TransitionContext(clientUserId, UserRole.Client, "RepairRequestCancelled", metadata, new { hadAssignedTechnician = previousTechnicianId.HasValue }),
            cancellationToken: cancellationToken);

        if (previousTechnicianId.HasValue)
        {
            await NotifySafeAsync(
                previousTechnicianId.Value,
                "RepairCancelled",
                "Solicitud cancelada por el cliente",
                $"El cliente canceló la solicitud de {repair.ApplianceName}.",
                $"/tecnico/servicios/{repair.Id}",
                cancellationToken);
        }

        var updated = await _repairs.GetByIdAsync(repair.Id, cancellationToken) ?? repair;
        return Result<RepairDetailDto>.Success(MapDetail(updated, clientUserId, conversationId: null));
    }

    public static string GetStatusLabel(RepairRequestStatus status) => status switch
    {
        RepairRequestStatus.Open => "Disponible",
        RepairRequestStatus.Assigned => "Asignada",
        RepairRequestStatus.Scheduled => "Programada",
        RepairRequestStatus.OnTheWay => "En camino",
        RepairRequestStatus.Arrived => "En el domicilio",
        RepairRequestStatus.InProgress => "En proceso",
        RepairRequestStatus.ClosureRequested => "Cierre solicitado",
        RepairRequestStatus.Completed => "Completado",
        RepairRequestStatus.Cancelled => "Cancelada",
        _ => "Desconocido"
    };

    private async Task<(RepairRequest? Repair, AppError? Error)> LoadActiveRepairAsync(
        Guid repairId,
        CancellationToken cancellationToken)
    {
        var repair = await _repairs.GetByIdAsync(repairId, cancellationToken);
        if (repair is null || repair.DeletedAt.HasValue)
        {
            return (null, new AppError(
                ErrorType.NotFound,
                "repair_not_found",
                "Solicitud de reparación no encontrada."));
        }

        return (repair, null);
    }

    private async Task<(RepairRequest? Repair, AppError? Error)> LoadForAssignedTechnicianAsync(
        Guid repairId,
        Guid technicianUserId,
        string forbiddenMessage,
        CancellationToken cancellationToken)
    {
        var (repair, error) = await LoadActiveRepairAsync(repairId, cancellationToken);
        if (error is not null)
        {
            return (null, error);
        }

        if (repair!.AssignedTechnicianId != technicianUserId)
        {
            return (null, new AppError(
                ErrorType.Forbidden,
                "repair_not_assigned_to_technician",
                forbiddenMessage));
        }

        return (repair, null);
    }

    private async Task<(RepairRequest? Repair, AppError? Error)> LoadForOwnerClientAsync(
        Guid repairId,
        Guid clientUserId,
        string forbiddenMessage,
        CancellationToken cancellationToken)
    {
        var (repair, error) = await LoadActiveRepairAsync(repairId, cancellationToken);
        if (error is not null)
        {
            return (null, error);
        }

        if (repair!.ClientUserId != clientUserId)
        {
            return (null, new AppError(
                ErrorType.Forbidden,
                "repair_not_owned",
                forbiddenMessage));
        }

        return (repair, null);
    }

    private Task LogAuditAsync(
        Guid repairId,
        TransitionContext transition,
        AuditOutcome outcome = AuditOutcome.Success,
        CancellationToken cancellationToken = default) =>
        _auditLogger.LogAsync(
            action: transition.AuditAction,
            entityName: "RepairRequest",
            entityId: repairId.ToString(),
            outcome: outcome,
            actorUserId: transition.ActorUserId,
            actorRole: transition.ActorRole.ToString(),
            metadata: transition.AuditMetadata,
            ipAddress: transition.Metadata.IpAddress,
            userAgent: transition.Metadata.UserAgent,
            cancellationToken: cancellationToken);

    private async Task<Result<RepairDetailDto>> SaveAuditNotifyAndMapAsync(
        RepairRequest repair,
        TransitionContext transition,
        NotificationSpec notification,
        bool includeConversation,
        CancellationToken cancellationToken)
    {
        await _repairs.SaveChangesAsync(cancellationToken);
        await LogAuditAsync(repair.Id, transition, cancellationToken: cancellationToken);

        if (notification.RecipientUserId.HasValue)
        {
            await NotifySafeAsync(
                notification.RecipientUserId.Value,
                notification.Type,
                notification.Title,
                notification.Body,
                notification.ActionUrl,
                cancellationToken);
        }

        Guid? conversationId = null;
        if (includeConversation)
        {
            var conversation = await _chat.GetByRepairRequestIdAsync(repair.Id, cancellationToken);
            conversationId = conversation?.Id;
        }

        return Result<RepairDetailDto>.Success(MapDetail(repair, transition.ActorUserId, conversationId));
    }

    private sealed record TransitionContext(
        Guid ActorUserId,
        UserRole ActorRole,
        string AuditAction,
        RequestMetadata Metadata,
        object? AuditMetadata = null);

    private sealed record NotificationSpec(
        Guid? RecipientUserId,
        string Type,
        string Title,
        string Body,
        string ActionUrl);

    private async Task NotifySafeAsync(
        Guid recipientUserId,
        string type,
        string title,
        string body,
        string actionUrl,
        CancellationToken cancellationToken)
    {
        if (_notifications is null)
        {
            return;
        }

        await _notifications.NotifyAsync(recipientUserId, type, title, body, actionUrl, cancellationToken);
    }

    private static string GenerateArrivalPin() =>
        RandomNumberGenerator.GetInt32(1000, 10000).ToString("D4");

    private static bool PinMatchesConstantTime(string expected, string supplied)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

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
        var normalized = NormalizeImageUrls(imageUrls);
        if (normalized.Length < RepairRequest.MinImages)
        {
            errors.Add($"Adjunta al menos {RepairRequest.MinImages} fotografía de referencia (máximo {RepairRequest.MaxImages}).");
            return errors;
        }

        if (normalized.Length > RepairRequest.MaxImages)
        {
            errors.Add($"Solo se permiten hasta {RepairRequest.MaxImages} imágenes por solicitud.");
            return errors;
        }

        foreach (var url in normalized)
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
        var isOwnerClient = actorUserId == repair.ClientUserId;
        var hasArrivalPin = !string.IsNullOrEmpty(repair.ArrivalPinCode);

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
            conversationId,
            repair.EnRouteAt,
            repair.EstimatedArrivalMinutes,
            repair.ArrivedAt,
            isOwnerClient ? repair.ArrivalPinCode : null,
            hasArrivalPin,
            repair.ClosureRejectionReason,
            repair.CancellationReason,
            FormatAvatarUrl(repair.AssignedTechnician),
            canSeeFullAddress ? FormatAvatarUrl(repair.ClientUser) : null);
    }

    private static string? FormatAvatarUrl(User? user)
    {
        if (user is null || user.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            return null;
        }

        return user.AvatarUrl;
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
