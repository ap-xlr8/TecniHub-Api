namespace TECNIHUB.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TECNIHUB.API";
    public string Audience { get; set; } = "TECNIHUB.Frontend";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
}
