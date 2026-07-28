using System.Net;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Notifications;

// Correos siempre en INGLÉS, con dos plantillas: reporte de problema (issue) y
// check-in operativo. Comparten el mismo encabezado/pie (shell).
public class EmailChannel : INotificationChannel
{
    private readonly IEmailSender _sender;
    public EmailChannel(IEmailSender sender) => _sender = sender;

    public string Name => "email";

    public bool IsEnabled(Tenant tenant)
        => tenant.EmailNotificationsEnabled && !string.IsNullOrWhiteSpace(tenant.NotificationEmails);

    public async Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
    {
        var to = Recipients(tenant);
        if (to.Length == 0) return;
        var (sevLabel, sevColor) = Severity(n.Severidad);
        var html = BuildIssueHtml(tenant.Nombre, n, sevLabel, sevColor);
        await _sender.SendAsync(to, IssueSubject(n.AssetCodigo, sevLabel), html, isHtml: true, ct);
    }

    public static string[] Recipients(Tenant tenant)
        => (tenant.NotificationEmails ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string IssueSubject(string codigo, string sevLabel)
        => $"[Valentino's] Issue reported · {codigo} · {sevLabel}";
    public static string OperationalSubject(string codigo)
        => $"[Valentino's] Operational check-in · {codigo}";

    // Previsualizaciones para la demo.
    public static string RenderIssuePreview(string tenantNombre, ReportCreatedNotification n)
    {
        var (label, color) = Severity(n.Severidad);
        return BuildIssueHtml(tenantNombre, n, label, color);
    }
    public static string RenderOperationalPreview(string tenantNombre, string codigo, string? by, string? nota, string when)
        => BuildOperationalHtml(tenantNombre, codigo, by, nota, when);

    private static (string label, string color) Severity(string severidad) => severidad switch
    {
        "NoFunciona" => ("Not working", "#dc2626"),
        "AMedias" => ("Partially working", "#d97706"),
        "Leve" => ("Minor issue", "#0ea5e9"),
        _ => (severidad, "#64748b")
    };

    // ---------- Plantilla: ISSUE ----------
    private static string BuildIssueHtml(string tenantNombre, ReportCreatedNotification n, string sevLabel, string sevColor)
    {
        var codigo = WebUtility.HtmlEncode(n.AssetCodigo);
        var ubic = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(n.Ubicacion) ? "—" : n.Ubicacion);
        var by = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(n.ReportadoPor) ? "—" : n.ReportadoPor);
        var descripcion = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(n.Descripcion) ? "—" : n.Descripcion).Replace("\n", "<br>");
        var sev = WebUtility.HtmlEncode(sevLabel);

        var rows = Row("Asset", codigo) + Row("Severity", sev) + Row("Location", ubic) + Row("Reported by", by);
        var body =
$@"        <tr><td style=""padding:26px 28px 6px;"">
          <div style=""display:inline-block;background:{sevColor};color:#ffffff;font-size:12px;font-weight:700;padding:5px 12px;border-radius:999px;text-transform:uppercase;letter-spacing:.4px;"">{sev}</div>
          <h1 style=""margin:14px 0 2px;color:#0f172a;font-size:20px;"">New issue reported</h1>
          <p style=""margin:0;color:#64748b;font-size:14px;"">A problem was reported on asset <strong style=""color:#0f172a;"">{codigo}</strong>.</p>
        </td></tr>
        <tr><td style=""padding:14px 28px 8px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"">{rows}</table></td></tr>
        <tr><td style=""padding:8px 28px 24px;"">
          <div style=""color:#64748b;font-size:13px;margin-bottom:6px;"">Description</div>
          <div style=""background:#f8fafc;border:1px solid #e5e9f0;border-radius:10px;padding:14px;color:#0f172a;font-size:14px;line-height:1.5;"">{descripcion}</div>
        </td></tr>";
        return Shell(tenantNombre, body);
    }

    // ---------- Plantilla: OPERATIONAL ----------
    public static string BuildOperationalHtml(string tenantNombre, string codigo, string? reportadoPor, string? nota, string when)
    {
        var cod = WebUtility.HtmlEncode(codigo);
        var by = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(reportadoPor) ? "—" : reportadoPor);
        var whenH = WebUtility.HtmlEncode(when);

        var rows = Row("Asset", cod) + Row("Status", "Operational") + Row("Checked by", by) + Row("Time", whenH);
        var noteBlock = string.IsNullOrWhiteSpace(nota) ? string.Empty :
$@"        <tr><td style=""padding:8px 28px 24px;"">
          <div style=""color:#64748b;font-size:13px;margin-bottom:6px;"">Notes</div>
          <div style=""background:#f8fafc;border:1px solid #e5e9f0;border-radius:10px;padding:14px;color:#0f172a;font-size:14px;line-height:1.5;"">{WebUtility.HtmlEncode(nota).Replace("\n", "<br>")}</div>
        </td></tr>";

        var body =
$@"        <tr><td style=""padding:26px 28px 6px;"">
          <div style=""display:inline-block;background:#16a34a;color:#ffffff;font-size:12px;font-weight:700;padding:5px 12px;border-radius:999px;text-transform:uppercase;letter-spacing:.4px;"">Operational</div>
          <h1 style=""margin:14px 0 2px;color:#0f172a;font-size:20px;"">Equipment checked — all good</h1>
          <p style=""margin:0;color:#64748b;font-size:14px;"">Asset <strong style=""color:#0f172a;"">{cod}</strong> was confirmed as operational.</p>
        </td></tr>
        <tr><td style=""padding:14px 28px 8px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"">{rows}</table></td></tr>
{noteBlock}";
        return Shell(tenantNombre, body);
    }

    private static string Row(string label, string value) =>
$@"<tr>
  <td style=""padding:10px 0;border-bottom:1px solid #e5e9f0;color:#64748b;font-size:13px;width:150px;vertical-align:top;"">{label}</td>
  <td style=""padding:10px 0;border-bottom:1px solid #e5e9f0;color:#0f172a;font-size:14px;font-weight:600;"">{value}</td>
</tr>";

    // Encabezado + pie compartidos (tablas + estilos inline para compatibilidad).
    private static string Shell(string tenantNombre, string bodyRows)
    {
        var tenant = WebUtility.HtmlEncode(tenantNombre);
        return
$@"<!doctype html>
<html>
<body style=""margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#eef2f7;padding:24px 12px;"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 6px 24px rgba(15,23,42,.08);"">
        <tr><td style=""background:#0b1f30;padding:22px 28px;"">
          <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0""><tr>
            <td style=""vertical-align:middle;width:48px;"">
              <div style=""width:44px;height:44px;border-radius:50%;background:radial-gradient(circle at 32% 28%,#123047,#0b1f30 70%);border:1px solid rgba(148,163,184,.3);text-align:center;line-height:44px;"">
                <span style=""font-family:Georgia,'Times New Roman',serif;font-style:italic;font-weight:700;font-size:26px;color:#d9bd6e;"">V</span>
              </div>
            </td>
            <td style=""vertical-align:middle;padding-left:12px;"">
              <div style=""color:#ffffff;font-size:16px;font-weight:700;letter-spacing:.2px;"">Valentino's</div>
              <div style=""color:#94a3b8;font-size:12px;"">Housekeeping · {tenant}</div>
            </td>
          </tr></table>
        </td></tr>
{bodyRows}
        <tr><td style=""background:#f8fafc;padding:18px 28px;border-top:1px solid #e5e9f0;"">
          <p style=""margin:0;color:#94a3b8;font-size:12px;"">This notification was generated automatically by Valentino's platform when the asset QR was scanned. Please do not reply to this email.</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
    }
}
