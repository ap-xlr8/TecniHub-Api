using TECNIHUB.Domain.Enums;

namespace TECNIHUB.Domain.Entities;

public sealed class SparePart
{
    public const int MinImages = 1;
    public const int MaxImages = 6;

    public Guid Id { get; set; }
    public Guid TechnicianUserId { get; set; }
    public User? TechnicianUser { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ApplianceCategory { get; set; } = string.Empty;
    public string? Compatibility { get; set; }

    public SparePartCondition Condition { get; set; } = SparePartCondition.Used;
    public SparePartModality Modality { get; set; } = SparePartModality.Sale;
    public decimal PriceMxn { get; set; }

    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactNotes { get; set; }

    public bool IsAvailable { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<SparePartImage> Images { get; set; } = [];

    public void SetAvailability(bool isAvailable, DateTimeOffset utcNow)
    {
        IsAvailable = isAvailable;
        UpdatedAt = utcNow;
    }

    public void SoftDelete(DateTimeOffset utcNow)
    {
        IsAvailable = false;
        DeletedAt = utcNow;
        UpdatedAt = utcNow;
    }
}
