namespace TECNIHUB.Domain.Entities;

public sealed class TechnicianProfile
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Specialties { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public string ServiceZone { get; set; } = string.Empty;
    public string? BusinessName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
