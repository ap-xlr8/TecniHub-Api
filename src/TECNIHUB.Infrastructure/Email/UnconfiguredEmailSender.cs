using Microsoft.Extensions.Logging;
using TECNIHUB.Application.Abstractions;

namespace TECNIHUB.Infrastructure.Email;

public sealed class UnconfiguredEmailSender : IEmailSender
{
    private readonly ILogger<UnconfiguredEmailSender> _logger;

    public UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendVerificationCodeAsync(
        string toEmail,
        string recipientName,
        string code,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "Intento de envío de código de verificación sin proveedor de correo configurado en entorno no-Development.");
        return Task.FromResult(false);
    }

    public Task<bool> SendPasswordResetCodeAsync(
        string toEmail,
        string recipientName,
        string code,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "Intento de envío de código de recuperación sin proveedor de correo configurado en entorno no-Development.");
        return Task.FromResult(false);
    }
}
