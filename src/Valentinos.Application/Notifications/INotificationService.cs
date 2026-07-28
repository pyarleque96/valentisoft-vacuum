namespace Valentinos.Application.Notifications;

public interface INotificationService
{
    Task NotifyReportCreatedAsync(ReportCreatedNotification n, CancellationToken ct = default);
}
