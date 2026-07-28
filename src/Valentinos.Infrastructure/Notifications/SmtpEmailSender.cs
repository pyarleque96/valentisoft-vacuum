using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Valentinos.Application.Notifications;

namespace Valentinos.Infrastructure.Notifications;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(SmtpOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
        CancellationToken ct = default)
    {
        // Apagado por default: sin SMTP configurado no se intenta enviar (no-op seguro).
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Host) || to.Count == 0)
            return;

        // Gmail exige que el remitente sea la cuenta autenticada.
        var from = string.IsNullOrWhiteSpace(_options.From) ? _options.User : _options.From;

        using var message = new MailMessage { From = new MailAddress(from), Subject = subject, Body = body, IsBodyHtml = isHtml };
        foreach (var addr in to) message.To.Add(addr);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true, // STARTTLS en el puerto 587
            Credentials = new NetworkCredential(_options.User, _options.Password)
        };

        try
        {
            await client.SendMailAsync(message, ct);
            _logger.LogInformation("📧 Email enviado a {To} vía {Host}:{Port}",
                string.Join(", ", to), _options.Host, _options.Port);
        }
        catch (Exception ex)
        {
            // NotificationService captura esto (best-effort); lo logueamos para diagnóstico.
            _logger.LogError(ex, "Fallo al enviar email SMTP a {To}", string.Join(", ", to));
            throw;
        }
    }
}
