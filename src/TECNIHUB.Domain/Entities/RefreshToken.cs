namespace TECNIHUB.Domain.Entities;

public sealed class RefreshToken
{
    public const int LifetimeDays = 7;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsActive(DateTimeOffset utcNow) => !RevokedAt.HasValue && ExpiresAt > utcNow;

    public void Revoke(DateTimeOffset utcNow, Guid? replacedByTokenId = null)
    {
        if (!RevokedAt.HasValue)
        {
            RevokedAt = utcNow;
        }

        if (replacedByTokenId.HasValue)
        {
            ReplacedByTokenId = replacedByTokenId;
        }

        UpdatedAt = utcNow;
    }
}
