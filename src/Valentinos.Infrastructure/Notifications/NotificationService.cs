using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Notifications;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly AppDbContext _db;

    public NotificationService(IEnumerable<INotificationChannel> channels, AppDbContext db)
    {
        _channels = channels;
        _db = db;
    }

    public async Task NotifyReportCreatedAsync(ReportCreatedNotification n, CancellationToken ct = default)
    {
        // Tenant no es ITenantOwned (sin query filter); se busca por Id directamente.
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == n.TenantId, ct);
        if (tenant is null) return;

        foreach (var channel in _channels)
        {
            if (!channel.IsEnabled(tenant)) continue;
            try
            {
                await channel.SendReportCreatedAsync(tenant, n, ct);
            }
            catch
            {
                // Best-effort: un canal que falla no debe romper el flujo ni los otros canales.
                // (En producción, loguear con el logger inyectado.)
            }
        }
    }
}
