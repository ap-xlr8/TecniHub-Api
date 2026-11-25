using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsEmailVerified { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.PendingVerification;

    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? ScheduledPurgeAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TechnicianProfile? TechnicianProfile { get; set; }
    public ICollection<VerificationCode> VerificationCodes { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public UserRole GetUserRole() => (UserRole)RoleId;

    public void MarkEmailVerified(DateTimeOffset utcNow)
    {
        IsEmailVerified = true;
        if (Status == AccountStatus.PendingVerification)
        {
            Status = AccountStatus.Active;
        }

        UpdatedAt = utcNow;
    }

    public void UpdatePasswordHash(string passwordHash, DateTimeOffset utcNow)
    {
        PasswordHash = passwordHash;
        UpdatedAt = utcNow;
    }

    public void RequestGradualDeletion(DateTimeOffset utcNow, TimeSpan retentionWindow)
    {
        Status = AccountStatus.PendingDeletion;
        DeletedAt = utcNow;
        ScheduledPurgeAt = utcNow.Add(retentionWindow);
        UpdatedAt = utcNow;
    }

    public void AnonymizePersonalData(DateTimeOffset utcNow)
    {
        var anonymizedToken = $"deleted-{Id:N}@anonymized.tecnihub.local";
        FirstName = "Usuario";
        LastName = "Eliminado";
        Email = anonymizedToken;
        NormalizedEmail = anonymizedToken.ToUpperInvariant();
        Phone = null;
        PasswordHash = string.Empty;
        Status = AccountStatus.Anonymized;
        UpdatedAt = utcNow;

        if (TechnicianProfile is not null)
        {
            TechnicianProfile.Specialties = "No disponible";
            TechnicianProfile.ServiceZone = "No disponible";
            TechnicianProfile.BusinessName = null;
            TechnicianProfile.ExperienceYears = 0;
            TechnicianProfile.UpdatedAt = utcNow;
        }
    }
}
