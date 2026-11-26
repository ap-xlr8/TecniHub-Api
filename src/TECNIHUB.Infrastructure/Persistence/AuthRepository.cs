using Microsoft.EntityFrameworkCore;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Domain.Entities;
using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Infrastructure.Persistence;

public sealed class AuthRepository : IAuthRepository
{
    private readonly TecniHubDbContext _dbContext;

    public AuthRepository(TecniHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        _dbContext.Users
            .Include(u => u.Role)
            .Include(u => u.TechnicianProfile)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<User?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Users
            .Include(u => u.Role)
            .Include(u => u.TechnicianProfile)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        _dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public async Task AddUserAsync(User user, CancellationToken cancellationToken = default) =>
        await _dbContext.Users.AddAsync(user, cancellationToken);

    public Task<VerificationCode?> GetLatestVerificationCodeAsync(
        Guid userId,
        VerificationPurpose purpose,
        CancellationToken cancellationToken = default) =>
        _dbContext.VerificationCodes
            .Where(vc => vc.UserId == userId && vc.Purpose == purpose)
            .OrderByDescending(vc => vc.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<VerificationCode?> GetActiveLockoutAsync(
        Guid userId,
        VerificationPurpose purpose,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        _dbContext.VerificationCodes
            .Where(vc => vc.UserId == userId
                && vc.Purpose == purpose
                && vc.LockedUntil.HasValue
                && vc.LockedUntil.Value > utcNow)
            .OrderByDescending(vc => vc.LockedUntil)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountRecentVerificationCodesAsync(
        Guid userId,
        VerificationPurpose purpose,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        _dbContext.VerificationCodes
            .CountAsync(
                vc => vc.UserId == userId
                    && vc.Purpose == purpose
                    && vc.CreatedAt >= sinceUtc,
                cancellationToken);

    public async Task AddVerificationCodeAsync(VerificationCode code, CancellationToken cancellationToken = default) =>
        await _dbContext.VerificationCodes.AddAsync(code, cancellationToken);

    public async Task InvalidatePendingCodesAsync(
        Guid userId,
        VerificationPurpose purpose,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var pending = await _dbContext.VerificationCodes
            .Where(vc => vc.UserId == userId
                && vc.Purpose == purpose
                && !vc.ConsumedAt.HasValue
                && vc.ExpiresAt > utcNow)
            .ToListAsync(cancellationToken);

        foreach (var code in pending)
        {
            code.MarkConsumed(utcNow);
        }
    }

    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
        await _dbContext.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _dbContext.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u!.Role)
            .Include(rt => rt.User)
                .ThenInclude(u => u!.TechnicianProfile)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

    public async Task RevokeAllUserRefreshTokensAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var activeTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.RevokedAt.HasValue)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(utcNow);
        }
    }

    public async Task<IReadOnlyList<User>> GetUsersPendingPurgeAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Users
            .Include(u => u.TechnicianProfile)
            .Where(u => u.Status == AccountStatus.PendingDeletion
                && u.ScheduledPurgeAt.HasValue
                && u.ScheduledPurgeAt.Value <= utcNow)
            .ToListAsync(cancellationToken);

    public Task<bool> IsUserActiveAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Status == AccountStatus.Active, cancellationToken);

    public async Task DeactivateActiveListingsForUserAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var openRepairs = await _dbContext.RepairRequests
            .Where(r => r.ClientUserId == userId && r.Status == RepairRequestStatus.Open && r.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var repair in openRepairs)
        {
            repair.Cancel(utcNow);
        }

        var activeSpareParts = await _dbContext.SpareParts
            .Where(p => p.TechnicianUserId == userId && p.IsAvailable && p.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var part in activeSpareParts)
        {
            part.SetAvailability(false, utcNow);
        }
    }

    public async Task AnonymizeUserOperationalPiiAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var repairs = await _dbContext.RepairRequests
            .Where(r => r.ClientUserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var repair in repairs)
        {
            if (repair.Status == RepairRequestStatus.Open)
            {
                repair.Cancel(utcNow);
            }

            repair.Street = "Domicilio anonimizado";
            repair.ExteriorNumber = null;
            repair.InteriorNumber = null;
            repair.AddressReference = null;
            repair.UpdatedAt = utcNow;
        }

        var parts = await _dbContext.SpareParts
            .Where(p => p.TechnicianUserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var part in parts)
        {
            part.IsAvailable = false;
            part.ContactPhone = null;
            part.ContactEmail = null;
            part.ContactNotes = null;
            part.UpdatedAt = utcNow;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
