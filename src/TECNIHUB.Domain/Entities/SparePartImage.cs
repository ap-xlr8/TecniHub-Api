namespace TECNIHUB.Domain.Entities;

public sealed class SparePartImage
{
    public Guid Id { get; set; }
    public Guid SparePartId { get; set; }
    public SparePart? SparePart { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
