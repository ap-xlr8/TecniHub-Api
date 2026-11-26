using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Domain.Entities;

public sealed class VerificationCode
{
    public const int ExpirationMinutes = 10;
    public const int MaxAttempts = 5;
    public const int LockoutHours = 1;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public VerificationPurpose Purpose { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsLocked(DateTimeOffset utcNow) => LockedUntil.HasValue && LockedUntil.Value > utcNow;

    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAt <= utcNow;

    public bool IsConsumed => ConsumedAt.HasValue;

    public bool RegisterFailedAttempt(DateTimeOffset utcNow)
    {
        AttemptCount++;
        UpdatedAt = utcNow;

        if (AttemptCount >= MaxAttempts)
        {
            LockedUntil = utcNow.AddHours(LockoutHours);
            return true;
        }

        return false;
    }

    public void MarkConsumed(DateTimeOffset utcNow)
    {
        ConsumedAt = utcNow;
        UpdatedAt = utcNow;
    }
}
