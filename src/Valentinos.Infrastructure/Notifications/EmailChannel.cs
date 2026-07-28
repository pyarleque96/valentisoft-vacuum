using System.Net;
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

        var (sevLabel, sevColor) = Severity(n.Severidad);
        var subject = $"[Valentino's] Nuevo reporte · {n.AssetCodigo} · {sevLabel}";
        var html = BuildHtml(tenant.Nombre, n, sevLabel, sevColor);

        await _sender.SendAsync(to, subject, html, isHtml: true, ct);
    }

    // Renderiza el HTML del correo para previsualización (usado por la demo).
    public static string RenderPreview(string tenantNombre, ReportCreatedNotification n)
    {
        var (label, color) = Severity(n.Severidad);
        return BuildHtml(tenantNombre, n, label, color);
    }

    private static (string label, string color) Severity(string severidad) => severidad switch
    {
        "NoFunciona" => ("No funciona", "#dc2626"),
        "AMedias" => ("Funciona a medias", "#d97706"),
        "Leve" => ("Leve", "#0ea5e9"),
        _ => (severidad, "#64748b")
    };

    // Plantilla HTML profesional, basada en tablas + estilos inline (compatibilidad
    // con clientes de correo). Valores dinámicos escapados para evitar inyección.
    private static string BuildHtml(string tenantNombre, ReportCreatedNotification n, string sevLabel, string sevColor)
    {
        var tenant = WebUtility.HtmlEncode(tenantNombre);
        var codigo = WebUtility.HtmlEncode(n.AssetCodigo);
        var ubicacion = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(n.Ubicacion) ? "—" : n.Ubicacion);
        var reportadoPor = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(n.ReportadoPor) ? "—" : n.ReportadoPor);
        var descripcion = WebUtility.HtmlEncode(n.Descripcion).Replace("\n", "<br>");
        var sev = WebUtility.HtmlEncode(sevLabel);

        string Row(string label, string value) =>
$@"<tr>
  <td style=""padding:10px 0;border-bottom:1px solid #e5e9f0;color:#64748b;font-size:13px;width:150px;vertical-align:top;"">{label}</td>
  <td style=""padding:10px 0;border-bottom:1px solid #e5e9f0;color:#0f172a;font-size:14px;font-weight:600;"">{value}</td>
</tr>";

        return
$@"<!doctype html>
<html>
<body style=""margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#eef2f7;padding:24px 12px;"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 6px 24px rgba(15,23,42,.08);"">

        <!-- Header / marca -->
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

        <!-- Título + severidad -->
        <tr><td style=""padding:26px 28px 6px;"">
          <div style=""display:inline-block;background:{sevColor};color:#ffffff;font-size:12px;font-weight:700;padding:5px 12px;border-radius:999px;text-transform:uppercase;letter-spacing:.4px;"">{sev}</div>
          <h1 style=""margin:14px 0 2px;color:#0f172a;font-size:20px;"">Nuevo reporte de avería</h1>
          <p style=""margin:0;color:#64748b;font-size:14px;"">Se reportó un problema en el activo <strong style=""color:#0f172a;"">{codigo}</strong>.</p>
        </td></tr>

        <!-- Detalles -->
        <tr><td style=""padding:14px 28px 8px;"">
          <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"">
            {Row("Activo", codigo)}
            {Row("Severidad", sev)}
            {Row("Ubicación", ubicacion)}
            {Row("Reportado por", reportadoPor)}
          </table>
        </td></tr>

        <!-- Descripción -->
        <tr><td style=""padding:8px 28px 24px;"">
          <div style=""color:#64748b;font-size:13px;margin-bottom:6px;"">Descripción</div>
          <div style=""background:#f8fafc;border:1px solid #e5e9f0;border-radius:10px;padding:14px;color:#0f172a;font-size:14px;line-height:1.5;"">{descripcion}</div>
        </td></tr>

        <!-- Footer -->
        <tr><td style=""background:#f8fafc;padding:18px 28px;border-top:1px solid #e5e9f0;"">
          <p style=""margin:0;color:#94a3b8;font-size:12px;"">Este aviso fue generado automáticamente por la plataforma de Valentino's al escanearse el QR del activo. No respondas a este correo.</p>
        </td></tr>

      </table>
    </td></tr>
  </table>
</body>
</html>";
    }
}
