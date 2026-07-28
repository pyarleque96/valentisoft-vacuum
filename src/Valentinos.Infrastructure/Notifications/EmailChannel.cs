using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Notifications;

public class EmailChannel : INotificationChannel
{
    private readonly IEmailSender _sender;
    public EmailChannel(IEmailSender sender) => _sender = sender;

    public string Name => "email";

    public bool IsEnabled(Tenant tenant)
        => tenant.EmailNotificationsEnabled && !string.IsNullOrWhiteSpace(tenant.NotificationEmails);

    public async Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
    {
        var to = (tenant.NotificationEmails ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (to.Length == 0) return;

        var subject = $"[Valentino's] Nueva avería reportada: {n.AssetCodigo} ({n.Severidad})";
        var body =
            $"Se reportó una avería en el activo {n.AssetCodigo}.\n" +
            $"Severidad: {n.Severidad}\n" +
            $"Ubicación: {n.Ubicacion ?? "-"}\n" +
            $"Reportado por: {n.ReportadoPor ?? "-"}\n\n" +
            $"Descripción:\n{n.Descripcion}\n";

        await _sender.SendAsync(to, subject, body, ct);
    }
}
