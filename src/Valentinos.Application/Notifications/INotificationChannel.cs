using Valentinos.Domain.Entities;

namespace Valentinos.Application.Notifications;

public interface INotificationChannel
{
    string Name { get; }
    bool IsEnabled(Tenant tenant);
    Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default);
}
