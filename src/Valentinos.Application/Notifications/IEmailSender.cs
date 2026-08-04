namespace Valentinos.Application.Notifications;

public record EmailAttachment(byte[] Content, string FileName, string ContentType);

public interface IEmailSender
{
    // includeConfiguredCc=false omite el CC configurado en SMTP (Smtp:Cc). Se usa para
    // envíos de prueba/sample que deben ir SOLO a los destinatarios explícitos de `to`.
    Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
        EmailAttachment? attachment = null, bool includeConfiguredCc = true, CancellationToken ct = default);
}
