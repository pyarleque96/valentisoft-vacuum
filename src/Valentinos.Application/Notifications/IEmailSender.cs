namespace Valentinos.Application.Notifications;

public interface IEmailSender
{
    Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
        CancellationToken ct = default);
}
