using TECNIHUB.Application.Common;

namespace TECNIHUB.Application.Auth.DTOs;

public sealed record RegisterClientRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Phone);

public sealed record RegisterTechnicianRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Phone,
    string Specialties,
    int ExperienceYears,
    string ServiceZone,
    string? BusinessName);

public sealed record VerifyEmailRequest(string Email, string Code);

public sealed record ResendVerificationRequest(string Email);

public sealed record LoginRequest(string Email, string Password);

public sealed record RequestPasswordResetRequest(string Email);

public sealed record ConfirmPasswordResetRequest(string Email, string Code, string NewPassword);

public sealed record TechnicianProfileDto(
    string Specialties,
    int ExperienceYears,
    string ServiceZone,
    string? BusinessName);

public sealed record UserSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Role,
    bool IsEmailVerified,
    TechnicianProfileDto? TechnicianProfile);

public sealed record AuthSessionResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserSummaryDto User);

public sealed record AuthPublicSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    UserSummaryDto User);

public sealed record RegistrationResponse(
    string Message,
    string Email,
    bool RequiresEmailVerification);

public sealed record GenericMessageResponse(string Message);

public interface IAuthService
{
    Task<Result<RegistrationResponse>> RegisterClientAsync(RegisterClientRequest request, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<RegistrationResponse>> RegisterTechnicianAsync(RegisterTechnicianRequest request, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<GenericMessageResponse>> VerifyEmailAsync(VerifyEmailRequest request, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<GenericMessageResponse>> ResendVerificationCodeAsync(ResendVerificationRequest request, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<AuthSessionResult>> LoginAsync(LoginRequest request, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<AuthSessionResult>> RefreshSessionAsync(string? rawRefreshToken, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<GenericMessageResponse>> LogoutAsync(string? rawRefreshToken, Guid? actorUserId, string? actorRole, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<GenericMessageResponse>> RequestPasswordResetAsync(RequestPasswordResetRequest request, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<GenericMessageResponse>> ConfirmPasswordResetAsync(ConfirmPasswordResetRequest request, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<GenericMessageResponse>> RequestAccountDeletionAsync(Guid userId, RequestMetadata metadata, CancellationToken cancellationToken = default);
    Task<Result<UserSummaryDto>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> ProcessScheduledAccountPurgesAsync(CancellationToken cancellationToken = default);
}
