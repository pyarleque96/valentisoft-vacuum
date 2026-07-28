using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Notifications;

// Canales diseñados pero apagados por default. Encendibles desde la config del
// tenant en el futuro (Plan: activación de WhatsApp/SMS/Twilio). No-op por ahora.

public class InAppChannel : INotificationChannel
{
    public string Name => "inapp";
    public bool IsEnabled(Tenant tenant) => tenant.InAppNotificationsEnabled;
    public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class WhatsAppChannel : INotificationChannel
{
    public string Name => "whatsapp";
    public bool IsEnabled(Tenant tenant) => tenant.WhatsAppNotificationsEnabled;
    public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class SmsChannel : INotificationChannel
{
    public string Name => "sms";
    public bool IsEnabled(Tenant tenant) => tenant.SmsNotificationsEnabled;
    public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
        => Task.CompletedTask;
}
