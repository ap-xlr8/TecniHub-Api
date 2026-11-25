using System.Security.Cryptography;
using System.Text;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Auth.DTOs;
using TECNIHUB.Application.Auth.Services;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.UnitTests;

public sealed class AuthServiceTests
{
    private readonly InMemoryAuthRepository _repository = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTokenService _tokens = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly FakeAuditLogger _auditLogger = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly RequestMetadata _metadata = new("127.0.0.1", "UnitTestAgent/1.0");
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(_repository, _hasher, _tokens, _emailSender, _auditLogger, _clock);
    }

    [Fact]
    public async Task RegisterClient_creates_pending_client_and_sends_verification_code()
    {
        var result = await _sut.RegisterClientAsync(
            new RegisterClientRequest("Sofía", "Ramírez", "sofia@example.com", "Clave#2026", null),
            _metadata);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresEmailVerification);
        Assert.Single(_repository.Users);
        Assert.Equal(UserRole.Client, _repository.Users[0].GetUserRole());
        Assert.Equal(AccountStatus.PendingVerification, _repository.Users[0].Status);
        Assert.NotEqual("Clave#2026", _repository.Users[0].PasswordHash);
        Assert.Single(_emailSender.SentVerificationCodes);
        Assert.Contains(_auditLogger.Entries, e => e.Action == "UserRegisteredClient" && e.Outcome == AuditOutcome.Success);
    }

    [Fact]
    public async Task RegisterClient_rejects_duplicate_email()
    {
        await _sut.RegisterClientAsync(
            new RegisterClientRequest("Sofía", "Ramírez", "sofia@example.com", "Clave#2026", null),
            _metadata);

        var duplicate = await _sut.RegisterClientAsync(
            new RegisterClientRequest("Otra", "Persona", "SOFIA@example.com", "Clave#2026", "5512345678"),
            _metadata);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorType.Conflict, duplicate.Error!.Type);
    }

    [Fact]
    public async Task RegisterTechnician_assigns_technician_role_and_profile()
    {
        var result = await _sut.RegisterTechnicianAsync(
            new RegisterTechnicianRequest(
                "Carlos",
                "Mendoza",
                "tecnico@example.com",
                "Clave#2026",
                "5512345678",
                "Refrigeradores, Lavadoras",
                6,
                "Zona Centro",
                "Servicio Express"),
            _metadata);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_repository.Users);
        Assert.Equal(UserRole.Technician, saved.GetUserRole());
        Assert.NotNull(saved.TechnicianProfile);
        Assert.Equal("Refrigeradores, Lavadoras", saved.TechnicianProfile!.Specialties);
        Assert.Equal(6, saved.TechnicianProfile.ExperienceYears);
    }

    [Fact]
    public async Task VerifyEmail_locks_for_one_hour_after_five_failed_attempts()
    {
        await _sut.RegisterClientAsync(
            new RegisterClientRequest("Ana", "Torres", "ana@example.com", "Clave#2026", null),
            _metadata);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var failed = await _sut.VerifyEmailAsync(new VerifyEmailRequest("ana@example.com", "000000"), _metadata);
            Assert.False(failed.IsSuccess);
            Assert.Equal(ErrorType.Validation, failed.Error!.Type);
        }

        var fifth = await _sut.VerifyEmailAsync(new VerifyEmailRequest("ana@example.com", "000000"), _metadata);
        Assert.False(fifth.IsSuccess);
        Assert.Equal(ErrorType.TooManyRequests, fifth.Error!.Type);

        var validCode = _emailSender.SentVerificationCodes[0].Code;
        var blockedEvenWithValidCode = await _sut.VerifyEmailAsync(new VerifyEmailRequest("ana@example.com", validCode), _metadata);
        Assert.False(blockedEvenWithValidCode.IsSuccess);
        Assert.Equal(ErrorType.TooManyRequests, blockedEvenWithValidCode.Error!.Type);
    }

    [Fact]
    public async Task VerifyEmail_rejects_expired_code_after_ten_minutes()
    {
        await _sut.RegisterClientAsync(
            new RegisterClientRequest("Luis", "Pérez", "luis@example.com", "Clave#2026", null),
            _metadata);

        var code = _emailSender.SentVerificationCodes[0].Code;
        _clock.Advance(TimeSpan.FromMinutes(11));

        var expired = await _sut.VerifyEmailAsync(new VerifyEmailRequest("luis@example.com", code), _metadata);

        Assert.False(expired.IsSuccess);
        Assert.Equal("expired_verification_code", expired.Error!.Code);
    }

    [Fact]
    public async Task Login_refresh_rotation_and_reuse_detection_work_as_expected()
    {
        await _sut.RegisterClientAsync(
            new RegisterClientRequest("Elena", "Cruz", "elena@example.com", "Clave#2026", null),
            _metadata);

        var unverifiedLogin = await _sut.LoginAsync(new LoginRequest("elena@example.com", "Clave#2026"), _metadata);
        Assert.False(unverifiedLogin.IsSuccess);
        Assert.Equal(ErrorType.Forbidden, unverifiedLogin.Error!.Type);

        var code = _emailSender.SentVerificationCodes[0].Code;
        var verified = await _sut.VerifyEmailAsync(new VerifyEmailRequest("elena@example.com", code), _metadata);
        Assert.True(verified.IsSuccess);

        var login = await _sut.LoginAsync(new LoginRequest("elena@example.com", "Clave#2026"), _metadata);
        Assert.True(login.IsSuccess);
        var firstRefresh = login.Value!.RefreshToken;

        var refreshed = await _sut.RefreshSessionAsync(firstRefresh, _metadata);
        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(firstRefresh, refreshed.Value!.RefreshToken);

        var reuseAttempt = await _sut.RefreshSessionAsync(firstRefresh, _metadata);
        Assert.False(reuseAttempt.IsSuccess);
        Assert.Equal("revoked_refresh_token", reuseAttempt.Error!.Code);
        Assert.All(_repository.RefreshTokens, t => Assert.NotNull(t.RevokedAt));
        Assert.Contains(_auditLogger.Entries, e => e.Action == "RefreshTokenReuseDetected" && e.Outcome == AuditOutcome.Blocked);
    }

    [Fact]
    public async Task PasswordReset_returns_generic_message_and_invalidates_code_after_use()
    {
        var unknown = await _sut.RequestPasswordResetAsync(new RequestPasswordResetRequest("noexiste@example.com"), _metadata);
        Assert.True(unknown.IsSuccess);
        Assert.Equal(AuthService.GenericPasswordResetMessage, unknown.Value!.Message);

        await _sut.RegisterClientAsync(
            new RegisterClientRequest("Diego", "Vega", "diego@example.com", "Clave#2026", null),
            _metadata);
        await _sut.VerifyEmailAsync(new VerifyEmailRequest("diego@example.com", _emailSender.SentVerificationCodes[0].Code), _metadata);

        var resetRequest = await _sut.RequestPasswordResetAsync(new RequestPasswordResetRequest("diego@example.com"), _metadata);
        Assert.True(resetRequest.IsSuccess);
        Assert.Equal(AuthService.GenericPasswordResetMessage, resetRequest.Value!.Message);

        var resetCode = _emailSender.SentPasswordResetCodes[0].Code;
        var confirm = await _sut.ConfirmPasswordResetAsync(
            new ConfirmPasswordResetRequest("diego@example.com", resetCode, "Nueva#2026"),
            _metadata);
        Assert.True(confirm.IsSuccess);

        var secondUse = await _sut.ConfirmPasswordResetAsync(
            new ConfirmPasswordResetRequest("diego@example.com", resetCode, "Otra#2026"),
            _metadata);
        Assert.False(secondUse.IsSuccess);

        var loginNew = await _sut.LoginAsync(new LoginRequest("diego@example.com", "Nueva#2026"), _metadata);
        Assert.True(loginNew.IsSuccess);
    }

    [Fact]
    public async Task GradualAccountDeletion_marks_pending_revokes_tokens_and_anonymizes_after_window()
    {
        await _sut.RegisterTechnicianAsync(
            new RegisterTechnicianRequest(
                "Mario",
                "López",
                "mario@example.com",
                "Clave#2026",
                "5599887766",
                "Microondas",
                4,
                "Zona Sur",
                "Taller Sur"),
            _metadata);

        await _sut.VerifyEmailAsync(new VerifyEmailRequest("mario@example.com", _emailSender.SentVerificationCodes[0].Code), _metadata);
        var login = await _sut.LoginAsync(new LoginRequest("mario@example.com", "Clave#2026"), _metadata);
        Assert.True(login.IsSuccess);

        var user = _repository.Users[0];
        var deleteResult = await _sut.RequestAccountDeletionAsync(user.Id, _metadata);
        Assert.True(deleteResult.IsSuccess);
        Assert.Equal(AccountStatus.PendingDeletion, user.Status);
        Assert.NotNull(user.DeletedAt);
        Assert.All(_repository.RefreshTokens, t => Assert.NotNull(t.RevokedAt));

        _clock.Advance(TimeSpan.FromDays(31));
        var purgedCount = await _sut.ProcessScheduledAccountPurgesAsync();

        Assert.Equal(1, purgedCount);
        Assert.Equal(AccountStatus.Anonymized, user.Status);
        Assert.Null(user.Phone);
        Assert.Equal("Usuario", user.FirstName);
        Assert.Contains(_auditLogger.Entries, e => e.Action == "AccountAnonymized");
    }

    private sealed class FakeClock(DateTimeOffset initial) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"HASH::{password}";
        public bool Verify(string password, string passwordHash) => passwordHash == $"HASH::{password}";
    }

    private sealed class FakeTokenService : ITokenService
    {
        private int _counter;

        public (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(User user) =>
            ($"jwt-{user.Id}-{++_counter}", DateTimeOffset.UtcNow.AddMinutes(15));

        public string GenerateRefreshToken() => $"rt-{Guid.NewGuid():N}";
        public string GenerateSixDigitCode() => $"{100000 + (++_counter):D6}";

        public string ComputeSha256Hash(string input) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

        public string ComputeScopedCodeHash(Guid codeId, Guid userId, VerificationPurpose purpose, string rawCode) =>
            ComputeSha256Hash($"{codeId:N}:{userId:N}:{(int)purpose}:{rawCode}");

        public bool FixedTimeEquals(string expectedHash, string actualHash) =>
            string.Equals(expectedHash, actualHash, StringComparison.Ordinal);
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string Email, string Code)> SentVerificationCodes { get; } = [];
        public List<(string Email, string Code)> SentPasswordResetCodes { get; } = [];

        public Task<bool> SendVerificationCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default)
        {
            SentVerificationCodes.Add((toEmail, code));
            return Task.FromResult(true);
        }

        public Task<bool> SendPasswordResetCodeAsync(string toEmail, string recipientName, string code, CancellationToken cancellationToken = default)
        {
            SentPasswordResetCodes.Add((toEmail, code));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<(string Action, AuditOutcome Outcome)> Entries { get; } = [];

        public Task LogAsync(
            string action,
            string entityName,
            string? entityId,
            AuditOutcome outcome,
            Guid? actorUserId = null,
            string? actorRole = null,
            object? metadata = null,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken cancellationToken = default)
        {
            Entries.Add((action, outcome));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuthRepository : IAuthRepository
    {
        public List<User> Users { get; } = [];
        public List<VerificationCode> VerificationCodes { get; } = [];
        public List<RefreshToken> RefreshTokens { get; } = [];

        public Task<User?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.NormalizedEmail == normalizedEmail));

        public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == userId));

        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.Any(u => u.NormalizedEmail == normalizedEmail));

        public Task AddUserAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }

        public Task<VerificationCode?> GetLatestVerificationCodeAsync(Guid userId, VerificationPurpose purpose, CancellationToken cancellationToken = default) =>
            Task.FromResult(VerificationCodes
                .Where(c => c.UserId == userId && c.Purpose == purpose)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefault());

        public Task<VerificationCode?> GetActiveLockoutAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
            Task.FromResult(VerificationCodes
                .Where(c => c.UserId == userId && c.Purpose == purpose && c.LockedUntil.HasValue && c.LockedUntil.Value > utcNow)
                .OrderByDescending(c => c.LockedUntil)
                .FirstOrDefault());

        public Task<int> CountRecentVerificationCodesAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(VerificationCodes.Count(c => c.UserId == userId && c.Purpose == purpose && c.CreatedAt >= sinceUtc));

        public Task AddVerificationCodeAsync(VerificationCode code, CancellationToken cancellationToken = default)
        {
            VerificationCodes.Add(code);
            return Task.CompletedTask;
        }

        public Task InvalidatePendingCodesAsync(Guid userId, VerificationPurpose purpose, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            foreach (var code in VerificationCodes.Where(c => c.UserId == userId && c.Purpose == purpose && !c.ConsumedAt.HasValue && c.ExpiresAt > utcNow))
            {
                code.MarkConsumed(utcNow);
            }

            return Task.CompletedTask;
        }

        public Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default)
        {
            token.User = Users.FirstOrDefault(u => u.Id == token.UserId);
            RefreshTokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(RefreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash));

        public Task RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            foreach (var token in RefreshTokens.Where(t => t.UserId == userId && !t.RevokedAt.HasValue))
            {
                token.Revoke(utcNow);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<User>> GetUsersPendingPurgeAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<User> result = Users
                .Where(u => u.Status == AccountStatus.PendingDeletion && u.ScheduledPurgeAt.HasValue && u.ScheduledPurgeAt.Value <= utcNow)
                .ToList();
            return Task.FromResult(result);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
