using Microsoft.EntityFrameworkCore;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Reports;

// Orquesta el envío diario de reportes: por cada tenant, un correo por cada site
// con destinatarios configurados (solo su data, sin CC), y luego el consolidado de
// todos los sites al tenant (con CC). Vive aparte del BackgroundService para poder
// probarlo sin temporizadores.
public class DailyReportRunner
{
    private readonly AppDbContext _db;
    private readonly ReportEmailer _emailer;
    private readonly ILogger<DailyReportRunner> _logger;

    public DailyReportRunner(AppDbContext db, ReportEmailer emailer, ILogger<DailyReportRunner> logger)
    {
        _db = db;
        _emailer = emailer;
        _logger = logger;
    }

    public async Task RunAsync(string period = "daily", DateTime? asOf = null, CancellationToken ct = default)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters().ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            var sites = await _db.Sites.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenant.Id && s.Emails != null && s.Emails != "")
                .OrderByDescending(s => s.Code)
                .ToListAsync(ct);

            // Un correo por site. Cada envío aislado: que falle uno no debe impedir
            // los demás ni el consolidado.
            foreach (var site in sites)
            {
                try
                {
                    var sent = await _emailer.SendSiteAsync(tenant, site, period, asOf, ct);
                    if (sent.Count > 0)
                        _logger.LogInformation("📍 Reporte del site {Site} enviado a {To}",
                            site.Code, string.Join(", ", sent));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fallo al enviar el reporte del site {Site} ({Tenant})",
                        site.Code, tenant.Nombre);
                }
            }

            if (string.IsNullOrWhiteSpace(tenant.NotificationEmails)) continue;

            try
            {
                var sent = await _emailer.SendAsync(tenant, period, asOf: asOf, ct: ct);
                if (sent.Count > 0)
                    _logger.LogInformation("📅 Reporte consolidado ({Tenant}) enviado a {To}",
                        tenant.Nombre, string.Join(", ", sent));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo al enviar el reporte consolidado de {Tenant}", tenant.Nombre);
            }
        }
    }
}
