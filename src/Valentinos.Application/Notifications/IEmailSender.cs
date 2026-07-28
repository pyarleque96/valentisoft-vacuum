namespace Valentinos.Application.Notifications;

public record EmailAttachment(byte[] Content, string FileName, string ContentType);

public interface IEmailSender
{
    Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
        EmailAttachment? attachment = null, CancellationToken ct = default);
}
