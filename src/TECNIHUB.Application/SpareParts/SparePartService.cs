using System.Net.Mail;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Application.Repairs;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Domain.Rules;

namespace TECNIHUB.Application.SpareParts;

public sealed class SparePartService : ISparePartService
{
    private const decimal MaxPriceMxn = 9_999_999.99m;

    private readonly ISparePartRepository _spareParts;
    private readonly IAuditLogger _auditLogger;
    private readonly IDateTimeProvider _clock;

    public SparePartService(
        ISparePartRepository spareParts,
        IAuditLogger auditLogger,
        IDateTimeProvider clock)
    {
        _spareParts = spareParts;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Result<SparePartDetailDto>> CreateAsync(
        Guid technicianUserId,
        UpsertSparePartInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var (errors, condition, modality) = ValidateInput(input);
        if (errors.Count > 0)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos de la refacción antes de publicar.",
                errors));
        }

        var utcNow = _clock.UtcNow;
        var partId = Guid.NewGuid();
        var imageUrls = NormalizeImageUrls(input.ImageUrls);

        var sparePart = new SparePart
        {
            Id = partId,
            TechnicianUserId = technicianUserId,
            Title = input.Title.Trim(),
            Description = input.Description.Trim(),
            ApplianceCategory = input.ApplianceCategory.Trim(),
            Compatibility = TrimOrNull(input.Compatibility),
            Condition = condition,
            Modality = modality,
            PriceMxn = Math.Round(input.PriceMxn, 2, MidpointRounding.AwayFromZero),
            ContactPhone = TrimOrNull(input.ContactPhone),
            ContactEmail = TrimOrNull(input.ContactEmail),
            ContactNotes = TrimOrNull(input.ContactNotes),
            IsAvailable = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            Images = imageUrls.Select((url, index) => new SparePartImage
            {
                Id = Guid.NewGuid(),
                SparePartId = partId,
                ImageUrl = url,
                DisplayOrder = index,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            }).ToList()
        };

        await _spareParts.AddAsync(sparePart, cancellationToken);
        await _spareParts.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "SparePartCreated",
            entityName: "SparePart",
            entityId: sparePart.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            metadata: new { priceMxn = sparePart.PriceMxn, imageCount = sparePart.Images.Count },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var loaded = await _spareParts.GetByIdAsync(sparePart.Id, cancellationToken) ?? sparePart;
        return Result<SparePartDetailDto>.Success(MapDetail(loaded, technicianUserId));
    }

    public async Task<Result<IReadOnlyList<SparePartSummaryDto>>> GetAvailableAsync(
        string? search,
        string? category,
        string? modality,
        string? condition = null,
        decimal? minPriceMxn = null,
        decimal? maxPriceMxn = null,
        CancellationToken cancellationToken = default)
    {
        SparePartModality? parsedModality = null;
        if (!string.IsNullOrWhiteSpace(modality)
            && Enum.TryParse<SparePartModality>(modality.Trim(), ignoreCase: true, out var m))
        {
            parsedModality = m;
        }

        SparePartCondition? parsedCondition = null;
        if (!string.IsNullOrWhiteSpace(condition)
            && Enum.TryParse<SparePartCondition>(condition.Trim(), ignoreCase: true, out var c))
        {
            parsedCondition = c;
        }

        var items = await _spareParts.GetAvailableAsync(
            search,
            category,
            parsedModality,
            parsedCondition,
            minPriceMxn,
            maxPriceMxn,
            cancellationToken);
        return Result<IReadOnlyList<SparePartSummaryDto>>.Success(items.Select(MapSummary).ToArray());
    }

    public async Task<Result<IReadOnlyList<SparePartSummaryDto>>> GetMineAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default)
    {
        var items = await _spareParts.GetByTechnicianAsync(technicianUserId, cancellationToken);
        return Result<IReadOnlyList<SparePartSummaryDto>>.Success(items.Select(MapSummary).ToArray());
    }

    public async Task<Result<SparePartDetailDto>> GetDetailAsync(
        Guid sparePartId,
        Guid actorTechnicianId,
        CancellationToken cancellationToken = default)
    {
        var part = await _spareParts.GetByIdAsync(sparePartId, cancellationToken);
        if (part is null || part.DeletedAt.HasValue)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "spare_part_not_found",
                "Publicación de refacción no encontrada."));
        }

        var isOwner = part.TechnicianUserId == actorTechnicianId;
        if (!part.IsAvailable && !isOwner)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "spare_part_unavailable",
                "Esta refacción ya no está disponible."));
        }

        return Result<SparePartDetailDto>.Success(MapDetail(part, actorTechnicianId));
    }

    public async Task<Result<SparePartDetailDto>> UpdateAsync(
        Guid sparePartId,
        Guid technicianUserId,
        UpsertSparePartInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var part = await _spareParts.GetByIdAsync(sparePartId, cancellationToken);
        if (part is null || part.DeletedAt.HasValue)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "spare_part_not_found",
                "Publicación de refacción no encontrada."));
        }

        if (part.TechnicianUserId != technicianUserId)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "spare_part_not_owned",
                "Solo el técnico oferente puede modificar esta publicación."));
        }

        var (errors, condition, modality) = ValidateInput(input);
        if (errors.Count > 0)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.Validation,
                "validation_error",
                "Revisa los datos de la refacción.",
                errors));
        }

        var utcNow = _clock.UtcNow;
        part.Title = input.Title.Trim();
        part.Description = input.Description.Trim();
        part.ApplianceCategory = input.ApplianceCategory.Trim();
        part.Compatibility = TrimOrNull(input.Compatibility);
        part.Condition = condition;
        part.Modality = modality;
        part.PriceMxn = Math.Round(input.PriceMxn, 2, MidpointRounding.AwayFromZero);
        part.ContactPhone = TrimOrNull(input.ContactPhone);
        part.ContactEmail = TrimOrNull(input.ContactEmail);
        part.ContactNotes = TrimOrNull(input.ContactNotes);
        part.UpdatedAt = utcNow;

        if (input.ImageUrls is not null)
        {
            var newImages = NormalizeImageUrls(input.ImageUrls)
                .Select((url, idx) => new SparePartImage
                {
                    Id = Guid.NewGuid(),
                    SparePartId = part.Id,
                    ImageUrl = url,
                    DisplayOrder = idx,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                })
                .ToArray();

            await _spareParts.ReplaceImagesAsync(part.Id, newImages, utcNow, cancellationToken);
        }

        await _spareParts.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "SparePartUpdated",
            entityName: "SparePart",
            entityId: part.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            metadata: new { priceMxn = part.PriceMxn },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        var loaded = await _spareParts.GetByIdAsync(part.Id, cancellationToken) ?? part;
        return Result<SparePartDetailDto>.Success(MapDetail(loaded, technicianUserId));
    }

    public async Task<Result<SparePartDetailDto>> SetAvailabilityAsync(
        Guid sparePartId,
        Guid technicianUserId,
        SetSparePartAvailabilityInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var part = await _spareParts.GetByIdAsync(sparePartId, cancellationToken);
        if (part is null || part.DeletedAt.HasValue)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.NotFound,
                "spare_part_not_found",
                "Publicación de refacción no encontrada."));
        }

        if (part.TechnicianUserId != technicianUserId)
        {
            return Result<SparePartDetailDto>.Failure(new AppError(
                ErrorType.Forbidden,
                "spare_part_not_owned",
                "Solo el técnico oferente puede cambiar la disponibilidad de esta publicación."));
        }

        var utcNow = _clock.UtcNow;
        part.SetAvailability(input.IsAvailable, utcNow);
        await _spareParts.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "SparePartAvailabilityChanged",
            entityName: "SparePart",
            entityId: part.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            metadata: new { isAvailable = part.IsAvailable },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<SparePartDetailDto>.Success(MapDetail(part, technicianUserId));
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid sparePartId,
        Guid technicianUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var part = await _spareParts.GetByIdAsync(sparePartId, cancellationToken);
        if (part is null || part.DeletedAt.HasValue)
        {
            return Result<bool>.Failure(new AppError(
                ErrorType.NotFound,
                "spare_part_not_found",
                "Publicación de refacción no encontrada."));
        }

        if (part.TechnicianUserId != technicianUserId)
        {
            return Result<bool>.Failure(new AppError(
                ErrorType.Forbidden,
                "spare_part_not_owned",
                "Solo el técnico oferente puede eliminar esta publicación."));
        }

        part.SoftDelete(_clock.UtcNow);
        await _spareParts.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            action: "SparePartDeleted",
            entityName: "SparePart",
            entityId: part.Id.ToString(),
            outcome: AuditOutcome.Success,
            actorUserId: technicianUserId,
            actorRole: nameof(UserRole.Technician),
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);

        return Result<bool>.Success(true);
    }

    private static (List<string> Errors, SparePartCondition Condition, SparePartModality Modality) ValidateInput(UpsertSparePartInput input)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 120)
        {
            errors.Add("El nombre o título de la refacción es obligatorio (máximo 120 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length < 10 || input.Description.Trim().Length > 1000)
        {
            errors.Add("La descripción es obligatoria (entre 10 y 1000 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(input.ApplianceCategory) || input.ApplianceCategory.Trim().Length > 80)
        {
            errors.Add("El tipo de aparato o categoría es obligatorio (máximo 80 caracteres).");
        }

        if (!string.IsNullOrWhiteSpace(input.Compatibility) && input.Compatibility.Trim().Length > 200)
        {
            errors.Add("La compatibilidad no debe superar 200 caracteres.");
        }

        if (!Enum.TryParse<SparePartCondition>(input.Condition?.Trim(), ignoreCase: true, out var condition)
            || !Enum.IsDefined(condition))
        {
            errors.Add("Selecciona una condición válida (Nueva, Usada o Reacondicionada).");
        }

        if (!Enum.TryParse<SparePartModality>(input.Modality?.Trim(), ignoreCase: true, out var modality)
            || !Enum.IsDefined(modality))
        {
            errors.Add("Selecciona una modalidad válida (Venta, Intercambio o Venta o intercambio).");
        }

        if (input.PriceMxn <= 0 || input.PriceMxn > MaxPriceMxn)
        {
            errors.Add("El precio en MXN es obligatorio y debe ser mayor a $0.");
        }

        var phone = TrimOrNull(input.ContactPhone);
        var email = TrimOrNull(input.ContactEmail);
        if (phone is null && email is null)
        {
            errors.Add("Indica al menos un medio de contacto (teléfono o correo) para que otros técnicos puedan acordar contigo.");
        }

        if (phone is not null && (phone.Length < 7 || phone.Length > 20))
        {
            errors.Add("El teléfono de contacto debe tener entre 7 y 20 caracteres.");
        }

        if (email is not null && !IsValidEmail(email))
        {
            errors.Add("El correo de contacto no tiene un formato válido.");
        }

        if (!string.IsNullOrWhiteSpace(input.ContactNotes) && input.ContactNotes.Trim().Length > 200)
        {
            errors.Add("Las notas de contacto no deben superar 200 caracteres.");
        }

        var normalizedImages = NormalizeImageUrls(input.ImageUrls);
        if (normalizedImages.Length < SparePart.MinImages)
        {
            errors.Add($"Adjunta al menos {SparePart.MinImages} fotografía de la refacción (máximo {SparePart.MaxImages}).");
        }
        else if (normalizedImages.Length > SparePart.MaxImages)
        {
            errors.Add($"Solo se permiten hasta {SparePart.MaxImages} imágenes por publicación.");
        }
        else if (normalizedImages.Any(u => !ImageSignatureValidator.IsAllowedHostedImageUrl(u)))
        {
            errors.Add("Todas las imágenes deben haberse cargado correctamente en el servicio de almacenamiento permitido.");
        }

        return (errors, condition, modality);
    }

    private static SparePartSummaryDto MapSummary(SparePart part)
    {
        var orderedUrls = part.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).ToArray();
        return new SparePartSummaryDto(
            part.Id,
            part.TechnicianUserId,
            FormatDisplayName(part.TechnicianUser),
            part.TechnicianUser?.TechnicianProfile?.ServiceZone,
            part.Title,
            Summarize(part.Description, 140),
            part.ApplianceCategory,
            part.Compatibility,
            part.Condition.ToString(),
            GetConditionLabel(part.Condition),
            part.Modality.ToString(),
            GetModalityLabel(part.Modality),
            part.PriceMxn,
            part.IsAvailable,
            part.IsAvailable ? "Disponible" : "No disponible",
            orderedUrls.Length,
            orderedUrls.FirstOrDefault(),
            part.CreatedAt,
            orderedUrls);
    }

    private static SparePartDetailDto MapDetail(SparePart part, Guid actorTechnicianId) => new(
        part.Id,
        part.TechnicianUserId,
        FormatDisplayName(part.TechnicianUser),
        part.TechnicianUser?.TechnicianProfile?.BusinessName,
        part.TechnicianUser?.TechnicianProfile?.ServiceZone,
        part.Title,
        part.Description,
        part.ApplianceCategory,
        part.Compatibility,
        part.Condition.ToString(),
        GetConditionLabel(part.Condition),
        part.Modality.ToString(),
        GetModalityLabel(part.Modality),
        part.PriceMxn,
        part.ContactPhone,
        part.ContactEmail,
        part.ContactNotes,
        part.IsAvailable,
        part.IsAvailable ? "Disponible" : "No disponible",
        part.CreatedAt,
        part.UpdatedAt,
        part.Images.OrderBy(i => i.DisplayOrder).Select(i => new ImageItemDto(i.Id, i.ImageUrl, i.DisplayOrder)).ToArray(),
        part.TechnicianUserId == actorTechnicianId,
        part.TechnicianUser is null || part.TechnicianUser.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized
            ? null
            : part.TechnicianUser.AvatarUrl);

    private static string GetConditionLabel(SparePartCondition condition) => condition switch
    {
        SparePartCondition.New => "Nueva",
        SparePartCondition.Used => "Usada",
        SparePartCondition.Refurbished => "Reacondicionada",
        _ => "Desconocida"
    };

    private static string GetModalityLabel(SparePartModality modality) => modality switch
    {
        SparePartModality.Sale => "Venta",
        SparePartModality.Exchange => "Intercambio",
        SparePartModality.SaleOrExchange => "Venta o intercambio",
        _ => "Venta"
    };

    private static string FormatDisplayName(User? user)
    {
        if (user is null)
        {
            return "Técnico";
        }

        if (user.Status is AccountStatus.PendingDeletion or AccountStatus.Anonymized)
        {
            return "Usuario eliminado";
        }

        return $"{user.FirstName} {user.LastName}".Trim();
    }

    private static bool IsValidEmail(string email)
    {
        if (email.Length > 254 || !email.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string[] NormalizeImageUrls(IReadOnlyList<string>? imageUrls) =>
        imageUrls is null
            ? []
            : imageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToArray();

    private static string Summarize(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..(maxLength - 1)].TrimEnd()}…";

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
