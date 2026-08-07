using Microsoft.EntityFrameworkCore;
using Valentinos.Api.Kpi;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Notifications;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Reports;

// Genera y envía por correo el reporte de KPIs (con el PDF adjunto). Dos salidas:
//  · SendSiteAsync   -> un site, a los destinatarios de ese site, SIN CC.
//  · SendAsync       -> todos los sites del tenant, a los destinatarios del tenant, CON CC.
// Reutilizado por el endpoint manual (/reports/{slug}/generate) y por el envío diario.
public class ReportEmailer
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<ReportEmailer> _logger;

    public ReportEmailer(AppDbContext db, IEmailSender email, ILogger<ReportEmailer> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    // Destinatarios del reporte de un site (TO directo, no CC).
    public static string[] SiteRecipients(Site site)
        => (site.Emails ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Reporte de UN site: solo su data, solo a sus destinatarios, sin el CC configurado.
    public async Task<IReadOnlyList<string>> SendSiteAsync(Tenant tenant, Site site, string period,
        DateTime? asOf = null, CancellationToken ct = default)
    {
        var to = SiteRecipients(site);
        if (to.Length == 0) return Array.Empty<string>();

        var now = asOf ?? DateTime.Now;
        var model = await BuildSiteKpiAsync(tenant, site.Id, site.Code, period, now, ct);

        return await SendModelsAsync(tenant, to, new[] { model },
            subjectSuffix: $" · Site {site.Code}", includeCc: false, now: now, ct: ct);
    }

    // Reporte CONSOLIDADO del tenant: todos sus sites, cada uno en su página del PDF.
    // `toOverride` fuerza los destinatarios (para pruebas); si es null, usa los del
    // tenant (NotificationEmails). `includeCc=false` omite el CC configurado. `asOf`
    // fija el "ahora" del cálculo (para simular el reporte de un día concreto).
    public async Task<IReadOnlyList<string>> SendAsync(Tenant tenant, string period,
        IReadOnlyList<string>? toOverride = null, bool includeCc = true,
        DateTime? asOf = null, CancellationToken ct = default)
    {
        var to = toOverride ?? EmailChannel.Recipients(tenant);
        if (to.Count == 0) return Array.Empty<string>();

        var now = asOf ?? DateTime.Now;

        var sites = await _db.Sites.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id)
            .OrderByDescending(s => s.Code)   // 127 HCC antes que 069
            .Select(s => new { s.Id, s.Code })
            .ToListAsync(ct);

        var models = new List<PeriodKpi>();
        foreach (var site in sites)
            models.Add(await BuildSiteKpiAsync(tenant, site.Id, site.Code, period, now, ct));

        return await SendModelsAsync(tenant, to, models,
            subjectSuffix: "", includeCc: includeCc, now: now, ct: ct);
    }

    // Arma el modelo KPI de un site: sus check-ins y sus "no disponibles" del periodo,
    // filtrados por SiteId. La ventana de 30 días cubre el periodo más largo (monthly);
    // Kpi.Compute recorta a la ventana real del periodo pedido.
    private async Task<PeriodKpi> BuildSiteKpiAsync(Tenant tenant, Guid siteId, string siteCode,
        string period, DateTime now, CancellationToken ct)
    {
        var since = now.Date.AddDays(-29);

        var checkins = await _db.StatusCheckins.IgnoreQueryFilters()
            .Where(c => c.SiteId == siteId && c.CreatedAt >= since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new KpiCheckin(c.EmployeeName, c.AssetCodigo, c.EstadoKey, c.Nota, c.CreatedAt))
            .ToListAsync(ct);

        var unav = await _db.UnavailableReports.IgnoreQueryFilters()
            .Where(u => u.SiteId == siteId && u.CreatedAt >= since)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new KpiUnavailable(u.EmployeeName, u.Nota, u.CreatedAt))
            .ToListAsync(ct);

        return Kpi.Kpi.Compute($"{tenant.Nombre} · Site {siteCode}", checkins, unav, now, period);
    }

    // Render + envío común a ambas salidas. Sin actividad en NINGÚN modelo no se envía
    // nada (coherente con el modal "sin data" del export a PDF).
    private async Task<IReadOnlyList<string>> SendModelsAsync(Tenant tenant, IReadOnlyList<string> to,
        IReadOnlyList<PeriodKpi> models, string subjectSuffix, bool includeCc, DateTime now, CancellationToken ct)
    {
        var totalActivity = models.Sum(m => m.Total + m.Un);
        if (models.Count == 0 || totalActivity == 0)
        {
            _logger.LogInformation("Sin actividad en el periodo para {Tenant}{Suffix}: no se envía reporte.",
                tenant.Nombre, subjectSuffix);
            return Array.Empty<string>();
        }

        var pName = models[0].Period switch { "weekly" => "Weekly", "monthly" => "Monthly", _ => "Daily" };
        var pdf = KpiPdf.RenderMulti(models);
        var subject = $"[ValentiSoft] {pName} report · {tenant.Nombre}{subjectSuffix}";
        var body = KpiHtml.RenderReportEmail(tenant.Nombre, models);
        var att = new EmailAttachment(pdf, $"KPI-{pName}-{now:yyyy-MM-dd}.pdf", "application/pdf");

        var handOff = DateTime.Now;
        await _email.SendAsync(to, subject, body, isHtml: true, attachment: att,
            includeConfiguredCc: includeCc, ct: ct);
        _logger.LogInformation("📄 Reporte {P} ({Sites} sites) enviado a {To} (PDF {Bytes} bytes) a las {HandOff:yyyy-MM-dd HH:mm:ss}",
            pName, models.Count, string.Join(", ", to), pdf.Length, handOff);
        return to;
    }
}
