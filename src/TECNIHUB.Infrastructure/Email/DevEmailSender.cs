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
            "[DEV-EMAIL] Envío de código de verificación simulado en desarrollo (vigencia: 10 min; configure Smtp para entrega real).");

        return Task.FromResult(true);
    }

    public Task<bool> SendPasswordResetCodeAsync(
        string toEmail,
        string recipientName,
        string code,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DEV-EMAIL] Envío de código de recuperación simulado en desarrollo (vigencia: 10 min; configure Smtp para entrega real).");

        return Task.FromResult(true);
    }
}
