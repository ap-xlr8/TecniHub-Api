using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string GenerateSixDigitCode();
    string ComputeSha256Hash(string input);
    string ComputeScopedCodeHash(Guid codeId, Guid userId, VerificationPurpose purpose, string rawCode);
    bool FixedTimeEquals(string expectedHash, string actualHash);
}

public interface IEmailSender
{
    Task<bool> SendVerificationCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default);
    Task<bool> SendPasswordResetCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default);
}

public interface IAuditLogger
{
    Task LogAsync(
        string action,
        string entityName,
        string? entityId,
        AuditOutcome outcome,
        Guid? actorUserId = null,
        string? actorRole = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);
}

public interface IAuthRepository
{
    Task<User?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task AddUserAsync(User user, CancellationToken cancellationToken = default);

    Task<VerificationCode?> GetLatestVerificationCodeAsync(Guid userId, VerificationPurpose purpose, CancellationToken cancellationToken = default);
    Task<VerificationCode?> GetActiveLockoutAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<int> CountRecentVerificationCodesAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task AddVerificationCodeAsync(VerificationCode code, CancellationToken cancellationToken = default);
    Task InvalidatePendingCodesAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> GetUsersPendingPurgeAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<bool> IsUserActiveAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    Task DeactivateActiveListingsForUserAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task AnonymizeUserOperationalPiiAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
