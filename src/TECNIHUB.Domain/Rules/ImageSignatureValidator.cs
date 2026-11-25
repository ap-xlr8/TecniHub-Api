namespace TECNIHUB.Domain.Rules;

public static class ImageSignatureValidator
{
    public const int MaxSizeBytes = 5 * 1024 * 1024; // 5 MB

    public static bool TryDetectFormat(ReadOnlySpan<byte> headerBytes, long totalLength, out string detectedMimeType, out string? errorMessage)
    {
        detectedMimeType = string.Empty;
        errorMessage = null;

        if (totalLength <= 0 || headerBytes.Length < 12)
        {
            errorMessage = "El archivo de imagen está vacío o es demasiado pequeño.";
            return false;
        }

        if (totalLength > MaxSizeBytes)
        {
            errorMessage = "La imagen supera el tamaño máximo permitido de 5 MB.";
            return false;
        }

        // JPEG: FF D8 FF
        if (headerBytes[0] == 0xFF && headerBytes[1] == 0xD8 && headerBytes[2] == 0xFF)
        {
            detectedMimeType = "image/jpeg";
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (headerBytes[0] == 0x89
            && headerBytes[1] == 0x50
            && headerBytes[2] == 0x4E
            && headerBytes[3] == 0x47
            && headerBytes[4] == 0x0D
            && headerBytes[5] == 0x0A
            && headerBytes[6] == 0x1A
            && headerBytes[7] == 0x0A)
        {
            detectedMimeType = "image/png";
            return true;
        }

        // WebP: RIFF....WEBP
        if (headerBytes[0] == 0x52
            && headerBytes[1] == 0x49
            && headerBytes[2] == 0x46
            && headerBytes[3] == 0x46
            && headerBytes[8] == 0x57
            && headerBytes[9] == 0x45
            && headerBytes[10] == 0x42
            && headerBytes[11] == 0x50)
        {
            detectedMimeType = "image/webp";
            return true;
        }

        errorMessage = "Formato de imagen no permitido. Usa únicamente archivos JPEG, PNG o WebP válidos.";
        return false;
    }

    public static bool IsAllowedHostedImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500)
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("i.ibb.co", StringComparison.OrdinalIgnoreCase)
            || host.Equals("ibb.co", StringComparison.OrdinalIgnoreCase);
    }
}
