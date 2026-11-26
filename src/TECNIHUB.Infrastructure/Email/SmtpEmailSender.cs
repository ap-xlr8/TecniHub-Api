using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TECNIHUB.Application.Abstractions;

namespace TECNIHUB.Infrastructure.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<bool> SendVerificationCodeAsync(
        string toEmail,
        string recipientName,
        string code,
        CancellationToken cancellationToken = default)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeCode = WebUtility.HtmlEncode(code);

        var subject = "TECNIHUB — Código de verificación de correo";
        var html = $"""
            <div style="font-family: system-ui, -apple-system, sans-serif; max-width: 520px; margin: 0 auto; color: #0f172a;">
              <h2 style="color: #0f3d6e;">Verifica tu cuenta en TECNIHUB</h2>
              <p>Hola {safeName},</p>
              <p>Usa el siguiente código de 6 dígitos para verificar tu correo electrónico:</p>
              <div style="font-size: 32px; font-weight: 800; letter-spacing: 6px; padding: 16px 24px; background: #edf2f8; border-radius: 8px; text-align: center; color: #0f3d6e; margin: 20px 0;">
                {safeCode}
              </div>
              <p style="font-size: 14px; color: #475569;">
                Este código vence en <strong>10 minutos</strong> y admite hasta 5 intentos. Si no solicitaste este registro, puedes ignorar este mensaje.
              </p>
            </div>
            """;
        var text = $"Hola {recipientName}, tu código de verificación en TECNIHUB es: {code}. Vence en 10 minutos.";

        return SendAsync(toEmail, subject, html, text, cancellationToken);
    }

    public Task<bool> SendPasswordResetCodeAsync(
        string toEmail,
        string recipientName,
        string code,
        CancellationToken cancellationToken = default)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeCode = WebUtility.HtmlEncode(code);

        var subject = "TECNIHUB — Código de recuperación de contraseña";
        var html = $"""
            <div style="font-family: system-ui, -apple-system, sans-serif; max-width: 520px; margin: 0 auto; color: #0f172a;">
              <h2 style="color: #0f3d6e;">Recuperación de contraseña</h2>
              <p>Hola {safeName},</p>
              <p>Recibimos una solicitud para restablecer la contraseña de tu cuenta en TECNIHUB. Ingresa el siguiente código de un solo uso:</p>
              <div style="font-size: 32px; font-weight: 800; letter-spacing: 6px; padding: 16px 24px; background: #edf2f8; border-radius: 8px; text-align: center; color: #0f3d6e; margin: 20px 0;">
                {safeCode}
              </div>
              <p style="font-size: 14px; color: #475569;">
                Este código vence en <strong>10 minutos</strong>, es de un solo uso y admite hasta 5 intentos. Si no solicitaste este cambio, ignora este correo y tu contraseña seguirá segura.
              </p>
            </div>
            """;
        var text = $"Hola {recipientName}, tu código de recuperación de contraseña en TECNIHUB es: {code}. Vence en 10 minutos y es de un solo uso.";

        return SendAsync(toEmail, subject, html, text, cancellationToken);
    }

    private async Task<bool> SendAsync(
        string toEmail,
        string subject,
        string html,
        string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Host)
            || string.IsNullOrWhiteSpace(_options.Username)
            || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogError("La configuración SMTP está incompleta (Host, Username o Password vacíos).");
            return false;
        }

        var fromAddress = string.IsNullOrWhiteSpace(_options.FromEmail)
            ? _options.Username
            : _options.FromEmail;

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, _options.FromName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = text,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(toEmail));
        message.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username, _options.Password),
            Timeout = 15_000
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Correo enviado correctamente vía SMTP ({Host}:{Port}).", _options.Host, _options.Port);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or OperationCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "No fue posible enviar el correo vía SMTP ({Host}:{Port}).", _options.Host, _options.Port);
            return false;
        }
    }
}
