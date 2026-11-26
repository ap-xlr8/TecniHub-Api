namespace TECNIHUB.Domain.Entities;

public sealed class RepairRequestImage
{
    public Guid Id { get; set; }
    public Guid RepairRequestId { get; set; }
    public RepairRequest? RepairRequest { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
