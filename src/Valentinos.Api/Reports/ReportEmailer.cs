using Microsoft.EntityFrameworkCore;
using Valentinos.Api.Kpi;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Notifications;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Reports;

// Genera y envía por correo el reporte de KPIs de un tenant (con el PDF adjunto).
// Reutilizado por el endpoint manual (/reports/{slug}/generate) y por el scheduler diario.
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

    // Envía el reporte del periodo. `toOverride` fuerza los destinatarios (para pruebas);
    // si es null, usa los del tenant (NotificationEmails). El CC lo agrega el SmtpEmailSender.
    // `toOverride` fuerza los destinatarios; `includeCc=false` omite el CC configurado
    // (para samples que van SOLO a `toOverride`); `asOf` fija el "ahora" del cálculo
    // (para simular el reporte de un día concreto). Devuelve null-si-vacío en un flag aparte.
    public async Task<IReadOnlyList<string>> SendAsync(Tenant tenant, string period,
        IReadOnlyList<string>? toOverride = null, bool includeCc = true,
        DateTime? asOf = null, CancellationToken ct = default)
    {
        var to = toOverride ?? EmailChannel.Recipients(tenant);
        if (to.Count == 0) return Array.Empty<string>();

        var now = asOf ?? DateTime.Now;
        var since = now.Date.AddDays(-29);

        // Un reporte POR SITE: se arma un modelo por cada sede del tenant, filtrando
        // sus check-ins/no-disponibles por SiteId. Solo se incluyen los sites CON
        // actividad en el periodo. El PDF junta todos (cada site en página nueva).
        var sites = await _db.Sites.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id)
            .OrderByDescending(s => s.Code)   // 069 primero
            .Select(s => new { s.Id, s.Code })
            .ToListAsync(ct);

        var models = new List<PeriodKpi>();
        var totalActivity = 0;
        foreach (var site in sites)
        {
            var checkins = await _db.StatusCheckins.IgnoreQueryFilters()
                .Where(c => c.SiteId == site.Id && c.CreatedAt >= since)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new KpiCheckin(c.EmployeeName, c.AssetCodigo, c.EstadoKey, c.Nota, c.CreatedAt))
                .ToListAsync(ct);
            var unav = await _db.UnavailableReports.IgnoreQueryFilters()
                .Where(u => u.SiteId == site.Id && u.CreatedAt >= since)
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new KpiUnavailable(u.EmployeeName, u.Nota, u.CreatedAt))
                .ToListAsync(ct);
            var model = Kpi.Kpi.Compute($"{tenant.Nombre} · Site {site.Code}", checkins, unav, now, period);
            models.Add(model);                       // todos los sites, cada uno su página
            totalActivity += model.Total + model.Un;
        }

        // Sin actividad en NINGÚN site: no se envía nada (coherente con el modal "sin data").
        if (models.Count == 0 || totalActivity == 0)
        {
            _logger.LogInformation("Sin actividad en el periodo para {Tenant}: no se envía reporte.", tenant.Nombre);
            return Array.Empty<string>();
        }

        var pName = models[0].Period switch { "weekly" => "Weekly", "monthly" => "Monthly", _ => "Daily" };
        var pdf = KpiPdf.RenderMulti(models);
        var subject = $"[ValentiSoft] {pName} report · {tenant.Nombre}";
        var body = KpiHtml.RenderReportEmail(tenant.Nombre, models);
        var att = new EmailAttachment(pdf, $"KPI-{pName}-{now:yyyy-MM-dd}.pdf", "application/pdf");

        await _email.SendAsync(to, subject, body, isHtml: true, attachment: att, includeConfiguredCc: includeCc, ct: ct);
        _logger.LogInformation("📄 Reporte {P} ({Sites} sites) enviado a {To} (PDF {Bytes} bytes)",
            pName, models.Count, string.Join(", ", to), pdf.Length);
        return to;
    }
}
