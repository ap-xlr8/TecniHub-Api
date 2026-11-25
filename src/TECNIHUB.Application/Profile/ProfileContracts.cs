using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Application.Common;

namespace TECNIHUB.Application.Profile;

public sealed record UpdateOwnProfileRequest(
    string FirstName,
    string LastName,
    string? Phone,
    string? Specialties = null,
    int? ExperienceYears = null,
    string? ServiceZone = null,
    string? BusinessName = null);

public interface IProfileService
{
    Task<Result<UserSummaryDto>> GetOwnProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserSummaryDto>> UpdateOwnProfileAsync(
        Guid userId,
        UpdateOwnProfileRequest request,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}
