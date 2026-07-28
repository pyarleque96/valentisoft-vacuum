using System.Net;
using System.Net.Mail;
using Valentinos.Application.Notifications;

namespace Valentinos.Infrastructure.Notifications;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    public SmtpEmailSender(SmtpOptions options) => _options = options;

    public async Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken ct = default)
    {
        // Apagado por default: sin SMTP configurado no se intenta enviar (no-op seguro).
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Host) || to.Count == 0)
            return;

        using var message = new MailMessage { From = new MailAddress(_options.From), Subject = subject, Body = body };
        foreach (var addr in to) message.To.Add(addr);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_options.User, _options.Password)
        };
        await client.SendMailAsync(message, ct);
    }
}
