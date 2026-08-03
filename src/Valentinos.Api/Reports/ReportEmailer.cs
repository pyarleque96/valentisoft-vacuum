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
    public async Task<IReadOnlyList<string>> SendAsync(Tenant tenant, string period,
        IReadOnlyList<string>? toOverride = null, CancellationToken ct = default)
    {
        var to = toOverride ?? EmailChannel.Recipients(tenant);
        if (to.Count == 0) return Array.Empty<string>();

        var since = DateTime.Now.Date.AddDays(-29);
        var checkins = await _db.StatusCheckins.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenant.Id && c.CreatedAt >= since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new KpiCheckin(c.EmployeeName, c.AssetCodigo, c.EstadoKey, c.Nota, c.CreatedAt))
            .ToListAsync(ct);
        var unav = await _db.UnavailableReports.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenant.Id && u.CreatedAt >= since)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new KpiUnavailable(u.EmployeeName, u.Nota, u.CreatedAt))
            .ToListAsync(ct);

        var model = Kpi.Kpi.Compute(tenant.Nombre, checkins, unav, DateTime.Now, period);
        var pdf = KpiPdf.Render(model);

        var pName = model.Period switch { "weekly" => "Weekly", "monthly" => "Monthly", _ => "Daily" };
        var subject = $"[ValentiSoft] {pName} report · {tenant.Nombre}";
        var body = KpiHtml.RenderReportEmail(model);
        var att = new EmailAttachment(pdf, $"KPI-{pName}-{DateTime.Now:yyyy-MM-dd}.pdf", "application/pdf");

        await _email.SendAsync(to, subject, body, isHtml: true, attachment: att, ct: ct);
        _logger.LogInformation("📄 Reporte {P} enviado a {To} (PDF {Bytes} bytes)", pName, string.Join(", ", to), pdf.Length);
        return to;
    }
}
