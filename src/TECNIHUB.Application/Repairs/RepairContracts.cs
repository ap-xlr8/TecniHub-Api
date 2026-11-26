using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Application.Repairs;

public sealed record PublicLocationDto(
    string PostalCode,
    string State,
    string Municipality,
    string? City,
    string Neighborhood);

public sealed record FullAddressDto(
    string Street,
    string? ExteriorNumber,
    string? InteriorNumber,
    string? AddressReference);

public sealed record ImageItemDto(
    Guid Id,
    string ImageUrl,
    int DisplayOrder);

public sealed record OpenRepairBoardItemDto(
    Guid Id,
    string ApplianceName,
    string? BrandModel,
    string IssueSummary,
    DateOnly PreferredDate,
    string PreferredTimeSlot,
    PublicLocationDto Location,
    string Status,
    string StatusLabel,
    int ImageCount,
    string? ThumbnailUrl,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string>? ImageUrls = null);

public sealed record ClientRepairSummaryDto(
    Guid Id,
    string ApplianceName,
    string? BrandModel,
    string IssueSummary,
    DateOnly PreferredDate,
    string PreferredTimeSlot,
    PublicLocationDto Location,
    string Status,
    string StatusLabel,
    string? AssignedTechnicianName,
    DateTimeOffset? ScheduledStartAt,
    DateTimeOffset? ClosureRequestedAt,
    DateTimeOffset? CompletedAt,
    int ImageCount,
    string? ThumbnailUrl,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string>? ImageUrls = null);

public sealed record ItineraryItemDto(
    Guid Id,
    string ApplianceName,
    string? BrandModel,
    string ClientDisplayName,
    PublicLocationDto Location,
    FullAddressDto FullAddress,
    DateOnly PreferredDate,
    string PreferredTimeSlot,
    DateTimeOffset? ScheduledStartAt,
    string Status,
    string StatusLabel,
    bool HasScheduleOverlap,
    DateTimeOffset AssignedAt,
    int ImageCount = 0,
    string? ThumbnailUrl = null,
    IReadOnlyList<string>? ImageUrls = null);

public sealed record RepairDetailDto(
    Guid Id,
    Guid ClientUserId,
    string ClientDisplayName,
    Guid? AssignedTechnicianId,
    string? AssignedTechnicianName,
    string ApplianceName,
    string? BrandModel,
    string IssueDescription,
    DateOnly PreferredDate,
    string PreferredTimeSlot,
    PublicLocationDto Location,
    FullAddressDto? FullAddress,
    string Status,
    string StatusLabel,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ScheduledStartAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ClosureRequestedAt,
    string? ClosureNotes,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ImageItemDto> Images,
    Guid? ConversationId,
    DateTimeOffset? EnRouteAt = null,
    int? EstimatedArrivalMinutes = null,
    DateTimeOffset? ArrivedAt = null,
    string? ClientArrivalPin = null,
    bool RequiresArrivalPin = false,
    string? ClosureRejectionReason = null,
    string? CancellationReason = null,
    string? AssignedTechnicianAvatarUrl = null,
    string? ClientAvatarUrl = null);

public sealed record OverlappingAppointmentDto(
    Guid RepairRequestId,
    string ApplianceName,
    DateTimeOffset ScheduledStartAt);

public sealed record ScheduleRepairResponse(
    RepairDetailDto Repair,
    bool HasScheduleOverlap,
    IReadOnlyList<OverlappingAppointmentDto> OverlappingAppointments);

public sealed record CreateRepairRequestInput(
    string ApplianceName,
    string? BrandModel,
    string IssueDescription,
    DateOnly PreferredDate,
    string PreferredTimeSlot,
    string PostalCode,
    string State,
    string Municipality,
    string? City,
    string Neighborhood,
    string Street,
    string? ExteriorNumber,
    string? InteriorNumber,
    string? AddressReference,
    IReadOnlyList<string>? ImageUrls);

public sealed record UpdateRepairImagesInput(IReadOnlyList<string> ImageUrls);
public sealed record ScheduleRepairInput(DateTimeOffset ScheduledStartAt);
public sealed record MarkOnTheWayInput(int? EstimatedArrivalMinutes);
public sealed record StartWorkInput(string? ArrivalPinCode);
public sealed record RequestClosureInput(string? ClosureNotes);
public sealed record RejectClosureInput(string Reason);
public sealed record ReleaseRepairInput(string Reason);
public sealed record CancelRepairInput(string? Reason);

public interface IRepairRequestService
{
    Task<Result<RepairDetailDto>> CreateAsync(Guid clientUserId, CreateRepairRequestInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ClientRepairSummaryDto>>> GetClientRequestsAsync(Guid clientUserId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<OpenRepairBoardItemDto>>> GetOpenBoardAsync(
        string? search,
        string? state = null,
        string? municipality = null,
        string? postalCode = null,
        CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ItineraryItemDto>>> GetTechnicianItineraryAsync(Guid technicianUserId, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> GetDetailAsync(Guid repairId, Guid actorUserId, UserRole actorRole, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> UpdateImagesAsync(Guid repairId, Guid clientUserId, UpdateRepairImagesInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> AcceptAsync(Guid repairId, Guid technicianUserId, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<ScheduleRepairResponse>> ScheduleAsync(Guid repairId, Guid technicianUserId, ScheduleRepairInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> MarkOnTheWayAsync(Guid repairId, Guid technicianUserId, MarkOnTheWayInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> MarkArrivedAsync(Guid repairId, Guid technicianUserId, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> StartWorkAsync(Guid repairId, Guid technicianUserId, RequestMetadata metadata, StartWorkInput? input = null, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> RequestClosureAsync(Guid repairId, Guid technicianUserId, RequestClosureInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> RejectClosureAsync(Guid repairId, Guid clientUserId, RejectClosureInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> ConfirmClosureAsync(Guid repairId, Guid clientUserId, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> ReleaseByTechnicianAsync(Guid repairId, Guid technicianUserId, ReleaseRepairInput input, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RepairDetailDto>> CancelAsync(Guid repairId, Guid clientUserId, RequestMetadata metadata, CancelRepairInput? input = null, CancellationToken cancellationToken = default);
}
