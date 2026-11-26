using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Application.Reviews;

public sealed record PublicReviewItemDto(
    Guid Id,
    Guid RepairRequestId,
    int Rating,
    string? Comment,
    string ApplianceName,
    string ReviewerDisplayName,
    DateTimeOffset CreatedAt);

public sealed record PublicTechnicianProfileDto(
    Guid TechnicianUserId,
    string DisplayName,
    string? BusinessName,
    string Specialties,
    int ExperienceYears,
    string ServiceZone,
    DateTimeOffset MemberSinceUtc,
    int CompletedServicesCount,
    int ReviewCount,
    decimal? AverageRating,
    IReadOnlyList<PublicReviewItemDto> Reviews);

public sealed record PublicClientProfileDto(
    Guid ClientUserId,
    string DisplayName,
    string? PublicRegion,
    DateTimeOffset MemberSinceUtc,
    int CompletedServicesCount);

public sealed record CreateServiceReviewInput(
    int Rating,
    string? Comment);

public interface IPublicProfileService
{
    Task<Result<PublicTechnicianProfileDto>> GetPublicTechnicianProfileAsync(
        Guid technicianUserId,
        CancellationToken cancellationToken = default);

    Task<Result<PublicClientProfileDto>> GetPublicClientProfileAsync(
        Guid clientUserId,
        Guid actorUserId,
        UserRole actorRole,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<Result<PublicReviewItemDto>> CreateReviewAsync(
        Guid repairRequestId,
        Guid clientUserId,
        CreateServiceReviewInput input,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<Result<PublicReviewItemDto?>> GetReviewForRepairAsync(
        Guid repairRequestId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}
