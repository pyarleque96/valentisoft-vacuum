using System.Net;
using System.Net.Mail;
using System.Net.Mime;
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
        EmailAttachment? attachment = null, CancellationToken ct = default)
    {
        // Apagado por default: sin SMTP configurado no se intenta enviar (no-op seguro).
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Host) || to.Count == 0)
            return;

        // Gmail exige que el remitente sea la cuenta autenticada. Usamos un nombre
        // visible ("Valentino's") como máscara: el destinatario ve el nombre, no el correo.
        var from = string.IsNullOrWhiteSpace(_options.From) ? _options.User : _options.From;
        var fromName = string.IsNullOrWhiteSpace(_options.FromName) ? "Valentino's" : _options.FromName;

        using var message = new MailMessage { From = new MailAddress(from, fromName), Subject = subject };
        foreach (var addr in to) message.To.Add(addr);

        // Copia (CC), configurable — separada por comas.
        if (!string.IsNullOrWhiteSpace(_options.Cc))
            foreach (var cc in _options.Cc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                message.CC.Add(cc);

        if (attachment is not null)
        {
            var stream = new MemoryStream(attachment.Content);
            message.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
        }

        if (isHtml)
        {
            // Vista HTML + logo incrustado por Content-ID (cid:vlogo) para que se
            // vea aunque el cliente bloquee imágenes externas.
            var htmlView = AlternateView.CreateAlternateViewFromString(body, null, MediaTypeNames.Text.Html);
            if (!string.IsNullOrWhiteSpace(_options.InlineLogoPath) && File.Exists(_options.InlineLogoPath))
            {
                var logo = new LinkedResource(_options.InlineLogoPath, new ContentType("image/jpeg"))
                {
                    ContentId = "vlogo",
                    TransferEncoding = TransferEncoding.Base64
                };
                htmlView.LinkedResources.Add(logo);
            }
            message.AlternateViews.Add(htmlView);
        }
        else
        {
            message.Body = body;
            message.IsBodyHtml = false;
        }

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
