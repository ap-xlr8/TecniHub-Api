namespace TECNIHUB.Domain.Entities;

public sealed class ServiceReview
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 500;

    public Guid Id { get; set; }
    public Guid RepairRequestId { get; set; }
    public RepairRequest? RepairRequest { get; set; }

    public Guid TechnicianUserId { get; set; }
    public User? TechnicianUser { get; set; }

    public Guid ClientUserId { get; set; }
    public User? ClientUser { get; set; }

    public int Rating { get; set; }
    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
