using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TECNIHUB.Application.Abstractions;
using TECNIHUB.Application.Common;
using TECNIHUB.Domain.Enums;
using TECNIHUB.Domain.Rules;

namespace TECNIHUB.Infrastructure.Media;

public sealed class ImgBBImageStorageService : IImageStorageService
{
    private readonly HttpClient _httpClient;
    private readonly ImgBBOptions _options;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<ImgBBImageStorageService> _logger;

    public ImgBBImageStorageService(
        HttpClient httpClient,
        IOptions<ImgBBOptions> options,
        IAuditLogger auditLogger,
        ILogger<ImgBBImageStorageService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<Result<string>> UploadImageAsync(
        Stream content,
        long contentLength,
        Guid actorUserId,
        string actorRole,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (contentLength > ImageSignatureValidator.MaxSizeBytes)
        {
            return Result<string>.Failure(new AppError(
                ErrorType.Validation,
                "image_too_large",
                "La imagen supera el tamaño máximo permitido de 5 MB."));
        }

        byte[] buffer;
        try
        {
            using var memory = new MemoryStream();
            var chunk = new byte[16 * 1024];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await content.ReadAsync(chunk, cancellationToken)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > ImageSignatureValidator.MaxSizeBytes)
                {
                    return Result<string>.Failure(new AppError(
                        ErrorType.Validation,
                        "image_too_large",
                        "La imagen supera el tamaño máximo permitido de 5 MB."));
                }

                memory.Write(chunk, 0, bytesRead);
            }

            buffer = memory.ToArray();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Error reading uploaded image stream.");
            return Result<string>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_image_stream",
                "No fue posible leer la imagen seleccionada. Intenta subirla nuevamente."));
        }

        if (!ImageSignatureValidator.TryDetectFormat(buffer.AsSpan(0, Math.Min(buffer.Length, 16)), buffer.LongLength, out var mimeType, out var errorMessage))
        {
            await _auditLogger.LogAsync(
                action: "MediaImageRejected",
                entityName: "MediaImage",
                entityId: null,
                outcome: AuditOutcome.Failure,
                actorUserId: actorUserId,
                actorRole: actorRole,
                metadata: new { sizeBytes = buffer.Length },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<string>.Failure(new AppError(
                ErrorType.Validation,
                "invalid_image_format",
                errorMessage ?? "Formato de imagen no permitido."));
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("ImgBB:ApiKey is not configured.");
            return Result<string>.Failure(new AppError(
                ErrorType.ServiceUnavailable,
                "media_service_unavailable",
                "El servicio de carga de imágenes no está disponible en este momento. Intenta nuevamente más tarde."));
        }

        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_options.ApiKey), "key");
            form.Add(new StringContent(Convert.ToBase64String(buffer)), "image");

            using var response = await _httpClient.PostAsync(_options.EndpointUrl, form, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ImgBB upload failed with HTTP status {StatusCode}.", (int)response.StatusCode);
                await LogUploadFailureAsync(actorUserId, actorRole, metadata, (int)response.StatusCode, cancellationToken);
                return Result<string>.Failure(new AppError(
                    ErrorType.ServiceUnavailable,
                    "image_upload_failed",
                    "No fue posible guardar la imagen. Por favor vuelve a subirla."));
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var success = root.TryGetProperty("success", out var successProp) && successProp.GetBoolean();
            string? imageUrl = null;
            if (success && root.TryGetProperty("data", out var dataProp))
            {
                if (dataProp.TryGetProperty("url", out var urlProp))
                {
                    imageUrl = urlProp.GetString();
                }
                else if (dataProp.TryGetProperty("display_url", out var displayUrlProp))
                {
                    imageUrl = displayUrlProp.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(imageUrl) || !ImageSignatureValidator.IsAllowedHostedImageUrl(imageUrl))
            {
                await LogUploadFailureAsync(actorUserId, actorRole, metadata, 502, cancellationToken);
                return Result<string>.Failure(new AppError(
                    ErrorType.ServiceUnavailable,
                    "image_upload_failed",
                    "No se recibió una dirección válida para la imagen. Intenta subirla nuevamente."));
            }

            await _auditLogger.LogAsync(
                action: "MediaImageUploaded",
                entityName: "MediaImage",
                entityId: null,
                outcome: AuditOutcome.Success,
                actorUserId: actorUserId,
                actorRole: actorRole,
                metadata: new { mimeType, sizeBytes = buffer.Length },
                ipAddress: metadata.IpAddress,
                userAgent: metadata.UserAgent,
                cancellationToken: cancellationToken);

            return Result<string>.Success(imageUrl.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "ImgBB image upload failed.");
            await LogUploadFailureAsync(actorUserId, actorRole, metadata, 503, cancellationToken);
            return Result<string>.Failure(new AppError(
                ErrorType.ServiceUnavailable,
                "image_upload_failed",
                "No fue posible conectar con el servicio de imágenes. Vuelve a intentarlo."));
        }
    }

    private Task LogUploadFailureAsync(
        Guid actorUserId,
        string actorRole,
        RequestMetadata metadata,
        int statusCode,
        CancellationToken cancellationToken) =>
        _auditLogger.LogAsync(
            action: "MediaImageUploadFailed",
            entityName: "MediaImage",
            entityId: null,
            outcome: AuditOutcome.Failure,
            actorUserId: actorUserId,
            actorRole: actorRole,
            metadata: new { statusCode },
            ipAddress: metadata.IpAddress,
            userAgent: metadata.UserAgent,
            cancellationToken: cancellationToken);
}
