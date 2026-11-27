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
    DateTimeOffset CreatedAt,
    IReadOnlyList<string>? ImageUrls = null);

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
    bool IsOwner,
    string? TechnicianAvatarUrl = null);

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

public sealed record SparePartBoardQuery(
    string? Search = null,
    string? Category = null,
    string? Modality = null,
    string? Condition = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int? Page = null,
    int? PageSize = null);

public interface ISparePartService
{
    Task<Result<SparePartDetailDto>> CreateAsync(Guid technicianUserId, UpsertSparePartInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<SparePartSummaryDto>>> GetAvailableAsync(
        SparePartBoardQuery query,
        CancellationToken cancellationToken = default);
    Task<Result<PagedResult<SparePartSummaryDto>>> GetMineAsync(
        Guid technicianUserId,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);
    Task<Result<SparePartDetailDto>> GetDetailAsync(Guid sparePartId, Guid actorTechnicianId, CancellationToken cancellationToken = default);
    Task<Result<SparePartDetailDto>> UpdateAsync(Guid sparePartId, Guid technicianUserId, UpsertSparePartInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<SparePartDetailDto>> SetAvailabilityAsync(Guid sparePartId, Guid technicianUserId, SetSparePartAvailabilityInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid sparePartId, Guid technicianUserId, RequestMetadata metadata, CancellationToken cancellationToken = default);
}
