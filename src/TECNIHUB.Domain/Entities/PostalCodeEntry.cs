namespace TECNIHUB.Domain.Entities;

public sealed class PostalCodeEntry
{
    public int Id { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string Neighborhood { get; set; } = string.Empty;
    public string SettlementType { get; set; } = string.Empty;
    public string Municipality { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? City { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
