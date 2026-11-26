namespace TECNIHUB.Infrastructure.Media;

public sealed class ImgBBOptions
{
    public const string SectionName = "ImgBB";

    public string ApiKey { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = "https://api.imgbb.com/1/upload";
}
