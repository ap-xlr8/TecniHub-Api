using TECNIHUB.Application.Common;
using TECNIHUB.Application.Repairs;

namespace TECNIHUB.Application.SpareParts;

public sealed record SparePartSummaryDto(
    Guid Id,
    Guid TechnicianUserId,
    string TechnicianDisplayName,
    string? TechnicianZone,
    string Title,
    string DescriptionSummary,
    string ApplianceCategory,
    string? Compatibility,
    string Condition,
    string ConditionLabel,
    string Modality,
    string ModalityLabel,
    decimal PriceMxn,
    bool IsAvailable,
    string AvailabilityLabel,
    int ImageCount,
    string? ThumbnailUrl,
    DateTimeOffset CreatedAt);

public sealed record SparePartDetailDto(
    Guid Id,
    Guid TechnicianUserId,
    string TechnicianDisplayName,
    string? TechnicianBusinessName,
    string? TechnicianZone,
    string Title,
    string Description,
    string ApplianceCategory,
    string? Compatibility,
    string Condition,
    string ConditionLabel,
    string Modality,
    string ModalityLabel,
    decimal PriceMxn,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactNotes,
    bool IsAvailable,
    string AvailabilityLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ImageItemDto> Images,
    bool IsOwner);

public sealed record UpsertSparePartInput(
    string Title,
    string Description,
    string ApplianceCategory,
    string? Compatibility,
    string Condition,
    string Modality,
    decimal PriceMxn,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactNotes,
    IReadOnlyList<string>? ImageUrls);

public sealed record SetSparePartAvailabilityInput(bool IsAvailable);

public interface ISparePartService
{
    Task<Result<SparePartDetailDto>> CreateAsync(Guid technicianUserId, UpsertSparePartInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<SparePartSummaryDto>>> GetAvailableAsync(string? search, string? category, string? modality, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<SparePartSummaryDto>>> GetMineAsync(Guid technicianUserId, CancellationToken cancellationToken = default);
    Task<Result<SparePartDetailDto>> GetDetailAsync(Guid sparePartId, Guid actorTechnicianId, CancellationToken cancellationToken = default);
    Task<Result<SparePartDetailDto>> UpdateAsync(Guid sparePartId, Guid technicianUserId, UpsertSparePartInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<SparePartDetailDto>> SetAvailabilityAsync(Guid sparePartId, Guid technicianUserId, SetSparePartAvailabilityInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
}
