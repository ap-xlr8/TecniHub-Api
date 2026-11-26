using Microsoft.Extensions.Logging;
using TECNIHUB.Application.Abstractions;

namespace TECNIHUB.Infrastructure.Email;

public sealed class DevEmailSender : IEmailSender
{
    private readonly ILogger<DevEmailSender> _logger;

    public DevEmailSender(ILogger<DevEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendVerificationCodeAsync(
        string toEmail,
        string recipientName,
        string code,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DEV-EMAIL] Verificación de correo para {Email} ({RecipientName}): código {Code} (vence en 10 min).",
            toEmail,
            recipientName,
            code);

        return Task.FromResult(true);
    }

    public Task<bool> SendPasswordResetCodeAsync(
        string toEmail,
        string recipientName,
        string code,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DEV-EMAIL] Recuperación de contraseña para {Email} ({RecipientName}): código {Code} (vence en 10 min).",
            toEmail,
            recipientName,
            code);

        return Task.FromResult(true);
    }
}
