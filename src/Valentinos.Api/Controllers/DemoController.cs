using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Qr;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Controllers;

// Controller SOLO para la demo end-to-end: sirve la página pública de reporte
// (la que abre el QR) y genera la imagen del QR apuntando a la URL pública real
// del request (funciona detrás del túnel de Cloudflare sin configuración).
[ApiController]
public class DemoController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IQrRenderer _qr;
    private readonly ILogger<DemoController> _logger;
    private readonly Valentinos.Application.Notifications.IEmailSender _email;
    private readonly Valentinos.Application.Reports.IReportService _reports;

    public DemoController(AppDbContext db, IQrRenderer qr, ILogger<DemoController> logger,
        Valentinos.Application.Notifications.IEmailSender email,
        Valentinos.Application.Reports.IReportService reports)
    {
        _db = db;
        _qr = qr;
        _logger = logger;
        _email = email;
        _reports = reports;
    }

    // Check-in de estado "operativo": el housekeeper confirma que el equipo funciona.
    // Se registra en el log y se envía el correo (plantilla operativa, en inglés).
    // Check-in unificado de estado (operativo / con fallas / fuera de servicio / sin
    // aspiradora). Registra el check-in (KPIs) y, si es un problema, crea el Report (fotos).
    [HttpPost("/api/public/{slug}/assets/{codigo}/operational")]
    public async Task<IActionResult> MarkOperational(string slug, string codigo,
        [FromForm] string? reportadoPor, [FromForm] string? nota, [FromForm] string? estado,
        [FromForm] IFormFileCollection? fotos)
    {
        var (tenant, asset, _) = await ResolveAsync(slug, codigo);
        if (tenant is null || asset is null) return NotFound();

        var estadoKey = string.IsNullOrWhiteSpace(estado) ? "operational" : estado;
        var by = string.IsNullOrWhiteSpace(reportadoPor) ? "-" : reportadoPor;

        // Persistir el check-in (alimenta la página de KPIs).
        _db.StatusCheckins.Add(new Domain.Entities.StatusCheckin
        {
            EmployeeName = by, AssetCodigo = asset.Codigo, EstadoKey = estadoKey, Nota = nota
        });
        await _db.SaveChangesAsync();

        // Si es un problema, crear el Report (guarda fotos; dispara email si estuviera activo).
        if ((estadoKey is "AMedias" or "NoFunciona")
            && Enum.TryParse<Domain.Enums.Severidad>(estadoKey, out var sev))
        {
            var photos = new List<Valentinos.Application.Reports.ReportPhotoInput>();
            foreach (var f in fotos ?? (IFormFileCollection)new FormFileCollection())
            {
                if (f.Length <= 0 || f.Length > 5 * 1024 * 1024) continue;
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms);
                photos.Add(new Valentinos.Application.Reports.ReportPhotoInput(ms.ToArray(), f.ContentType));
            }
            try
            {
                await _reports.CreateReportAsync(new Valentinos.Application.Reports.CreateReportRequest(
                    asset.Codigo, string.IsNullOrWhiteSpace(nota) ? "-" : nota, sev, null, by, photos));
            }
            catch (Exception ex) { _logger.LogError(ex, "Fallo al crear Report del check-in"); }
        }

        _logger.LogInformation("✅ CHECK-IN {Estado}: {Codigo} ({Tenant}) por {Por}. Nota: {Nota}",
            estadoKey, asset.Codigo, tenant.Nombre, by,
            string.IsNullOrWhiteSpace(nota) ? "-" : nota);

        // Correo de check-in operativo (mismo canal/config de email que los reportes).
        if (tenant.EmailNotificationsEnabled)
        {
            var to = Valentinos.Infrastructure.Notifications.EmailChannel.Recipients(tenant);
            if (to.Length > 0)
            {
                var html = Valentinos.Infrastructure.Notifications.EmailChannel.OperationalEmailHtml(
                    tenant.Nombre, asset.Codigo, reportadoPor, nota, DateTime.Now.ToString("g"));
                var subject = Valentinos.Infrastructure.Notifications.EmailChannel.OperationalSubject(asset.Codigo);
                try { await _email.SendAsync(to, subject, html, isHtml: true); }
                catch (Exception ex) { _logger.LogError(ex, "Fallo email operativo"); }
            }
        }
        return Ok(new { codigo = asset.Codigo, estado = "Operativo" });
    }

    // URL pública real del request: prioriza los headers que inyecta Cloudflare
    // (X-Forwarded-Proto / X-Forwarded-Host) y cae a los del request directo.
    private string PublicBaseUrl()
    {
        var proto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        proto = string.IsNullOrWhiteSpace(proto) ? Request.Scheme : proto.Split(',')[0].Trim();
        host = string.IsNullOrWhiteSpace(host) ? Request.Host.Value : host.Split(',')[0].Trim();
        return $"{proto}://{host}";
    }

    // Imagen PNG del QR de un activo, codificando la URL pública del formulario.
    // `base` permite forzar la URL base (la landing la pasa explícitamente para
    // que el QR use la URL del túnel aunque el sub-request de la imagen no herede
    // los headers X-Forwarded-*).
    [HttpGet("api/public/{slug}/assets/{codigo}/qr.png")]
    public async Task<IActionResult> QrPng(string slug, string codigo, [FromQuery] string? @base)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();

        var asset = await _db.Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.Codigo == codigo);
        if (asset is null) return NotFound();

        byte[]? logo = null; // el tenant de demo no tiene logo; el QR se genera sin marca de agua
        var baseUrl = string.IsNullOrWhiteSpace(@base) ? PublicBaseUrl() : @base.TrimEnd('/');
        var url = $"{baseUrl}/r/{slug}/{codigo}";
        var png = _qr.RenderPngForUrl(url, codigo, logo);
        return File(png, "image/png");
    }

    // Página pública de reporte que abre el QR. HTML autocontenido; postea al
    // endpoint público existente POST /api/public/{slug}/reports.
    // Página de bienvenida que abre el QR: logo de Valentino's + botón para reportar.
    [HttpGet("/r/{slug}/{codigo}")]
    public async Task<IActionResult> Intro(string slug, string codigo)
    {
        var (tenant, asset, tipo) = await ResolveAsync(slug, codigo);
        if (tenant is null) return NotFound();
        if (asset is null) return Content(NotFoundHtml(codigo), "text/html; charset=utf-8");

        return Content(IntroHtml(tenant.Nombre, slug, codigo, tipo?.Nombre ?? "Activo"),
            "text/html; charset=utf-8");
    }

    // Formulario de reporte (se llega desde el botón de la página de bienvenida).
    [HttpGet("/r/{slug}/{codigo}/reportar")]
    public async Task<IActionResult> ReportForm(string slug, string codigo)
    {
        var (tenant, asset, tipo) = await ResolveAsync(slug, codigo);
        if (tenant is null) return NotFound();
        if (asset is null) return Content(NotFoundHtml(codigo), "text/html; charset=utf-8");

        return Content(FormHtml(tenant.Nombre, slug, codigo, tipo?.Nombre ?? "Activo"),
            "text/html; charset=utf-8");
    }

    private async Task<(Domain.Entities.Tenant? tenant, Domain.Entities.Asset? asset, Domain.Entities.AssetType? tipo)>
        ResolveAsync(string slug, string codigo)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return (null, null, null);

        var asset = await _db.Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.Codigo == codigo);
        if (asset is null) return (tenant, null, null);

        var tipo = await _db.AssetTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == asset.AssetTypeId);
        return (tenant, asset, tipo);
    }


    // Lista de empleados del tenant (para el autocompletar del formulario).
    [HttpGet("/api/public/{slug}/employees")]
    public async Task<IActionResult> Employees(string slug)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();

        var names = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenant.Id)
            .OrderBy(e => e.Nombre)
            .Select(e => e.Nombre)
            .ToListAsync();
        return Ok(names);
    }

    // Previsualización del correo (demo): plantilla de ISSUE (en inglés).
    [HttpGet("/demo/email-preview")]
    public IActionResult EmailPreview()
    {
        var sample = new Valentinos.Application.Notifications.ReportCreatedNotification(
            Guid.Empty, "VAC-001", "NoFunciona",
            "The vacuum won't turn on and makes a loud noise when plugged in.",
            "Floor 3", "Ana");
        var html = Valentinos.Infrastructure.Notifications.EmailChannel.RenderIssuePreview("MasterCorp", sample);
        return Content(html, "text/html; charset=utf-8");
    }

    // Previsualización del correo (demo): plantilla OPERATIONAL (en inglés).
    [HttpGet("/demo/email-preview-operational")]
    public IActionResult EmailPreviewOperational()
    {
        var html = Valentinos.Infrastructure.Notifications.EmailChannel.RenderOperationalPreview(
            "MasterCorp", "VAC-001", "Ana", "Cleaned filter, working fine.", DateTime.Now.ToString("g"));
        return Content(html, "text/html; charset=utf-8");
    }

    // ---------- Página de KPIs / reportes (ligada al tenant) ----------
    private async Task<(Domain.Entities.Tenant? tenant, List<Kpi.KpiCheckin> checkins)> LoadCheckinsAsync(string slug)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return (null, new List<Kpi.KpiCheckin>());
        var since = DateTime.Now.Date.AddDays(-29);
        var list = await _db.StatusCheckins.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenant.Id && c.CreatedAt >= since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new Kpi.KpiCheckin(c.EmployeeName, c.AssetCodigo, c.EstadoKey, c.Nota, c.CreatedAt))
            .ToListAsync();
        return (tenant, list);
    }

    [HttpGet("/reports/{slug}")]
    public async Task<IActionResult> KpiPage(string slug)
    {
        var (tenant, list) = await LoadCheckinsAsync(slug);
        if (tenant is null) return NotFound();
        var model = Kpi.Kpi.Compute(tenant.Nombre, list, DateTime.Now);
        return Content(Kpi.KpiHtml.Render(model, slug), "text/html; charset=utf-8");
    }

    // Previsualización del PDF del reporte (mismo contenido que la página / el adjunto).
    [HttpGet("/reports/{slug}/pdf")]
    public async Task<IActionResult> KpiPdfPreview(string slug)
    {
        var (tenant, list) = await LoadCheckinsAsync(slug);
        if (tenant is null) return NotFound();
        var model = Kpi.Kpi.Compute(tenant.Nombre, list, DateTime.Now);
        return File(Kpi.KpiPdf.Render(model), "application/pdf");
    }

    // Genera el reporte diario y lo envía por correo con el PDF adjunto (acción manual).
    [HttpPost("/reports/{slug}/generate")]
    public async Task<IActionResult> KpiGenerate(string slug)
    {
        var (tenant, list) = await LoadCheckinsAsync(slug);
        if (tenant is null) return NotFound();

        var model = Kpi.Kpi.Compute(tenant.Nombre, list, DateTime.Now);
        var pdf = Kpi.KpiPdf.Render(model);
        var to = Valentinos.Infrastructure.Notifications.EmailChannel.Recipients(tenant);
        if (to.Length == 0) return Ok(new { to = (string?)null });

        var subject = $"[Valentino's] Daily report generated · {tenant.Nombre}";
        var body =
            $"<div style=\"font-family:Segoe UI,Arial,sans-serif;color:#334155;font-size:14px;\">" +
            $"<h2 style=\"color:#1560A8;\">Daily report generated</h2>" +
            $"<p>The daily KPI report for <b>{WebUtility.HtmlEncode(tenant.Nombre)}</b> has been generated on {model.GeneratedAt:g}.</p>" +
            $"<p>Today — Operational: {model.DOperational} · With faults: {model.DFaults} · Out of service: {model.DOutOfService} · No vacuum: {model.DUnavailable} · Total: {model.DTotal}.</p>" +
            $"<p>The full report is attached as a PDF.</p></div>";
        var att = new Valentinos.Application.Notifications.EmailAttachment(
            pdf, $"KPI-Report-{DateTime.Now:yyyy-MM-dd}.pdf", "application/pdf");

        try { await _email.SendAsync(to, subject, body, isHtml: true, attachment: att); }
        catch (Exception ex) { _logger.LogError(ex, "Fallo al enviar reporte diario"); return StatusCode(500, new { error = "email failed" }); }

        _logger.LogInformation("📄 Reporte diario generado y enviado a {To} (PDF {Bytes} bytes)", string.Join(", ", to), pdf.Length);
        return Ok(new { to = string.Join(", ", to) });
    }

    // Landing de demo: muestra el QR y el enlace del formulario para el activo semilla.
    [HttpGet("/demo/{slug}")]
    public async Task<IActionResult> DemoLanding(string slug)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();

        var assets = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenant.Id)
            .OrderBy(a => a.Codigo)
            .Select(a => a.Codigo)
            .ToListAsync();

        return Content(LandingHtml(tenant.Nombre, slug, assets, PublicBaseUrl()),
            "text/html; charset=utf-8");
    }

    private static string Layout(string title, string bodyInner, string wrapClass = "") =>
$@"<!doctype html>
<html lang=""es"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>{title}</title>
<style>
  :root {{ color-scheme: light; }}
  body {{ font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 0;
         min-height: 100vh; min-height: 100dvh; display: flex;
         background: #eef1f5; color: #334155; }}
  /* margin:auto centra vertical y horizontalmente; si el contenido es más alto
     que la pantalla, los márgenes colapsan y hace scroll sin recortar. */
  .wrap {{ max-width: 560px; width: 100%; margin: auto; padding: 24px 18px;
          box-sizing: border-box; }}
  /* Variante 'fill': ocupa todo el alto y ancla el botón de enviar al fondo. */
  .wrap.fill {{ margin: 0 auto; min-height: 100dvh; display: flex; flex-direction: column; }}
  .wrap.fill > .card {{ flex: 1; display: flex; flex-direction: column; }}
  .wrap.fill form {{ display: flex; flex-direction: column; flex: 1; }}
  .wrap.fill form button[type=""submit""] {{ margin-top: auto; }}
  /* Gap antes del botón cuando fotos es el último campo (modo problema). */
  .wrap.fill form #problemFields {{ margin-bottom: 20px; }}
  /* En modo operativo (notas último), el margen lo agrega onStatus(). */
  .wrap.fill form.op textarea {{ margin-bottom: 20px; }}
  .card {{ background: #ffffff; border: 1px solid #e5e9f0; border-radius: 16px; padding: 22px;
          box-shadow: 0 10px 30px rgba(15,23,42,.06); }}
  h1 {{ font-size: 20px; margin: 0 0 4px; color:#2b3440; }}
  .muted {{ color: #64748b; font-size: 14px; }}
  .badge {{ display:inline-block; background:#eaf1f9; color:#1560A8; border-radius:999px;
           padding:5px 12px; font-weight:700; font-size:14px; margin-top:8px; }}
  label {{ display:block; font-size:14px; margin:16px 0 6px; font-weight:600; color:#334155; }}
  input, textarea, select {{ width:100%; box-sizing:border-box; padding:12px; border-radius:10px;
    border:1px solid #cbd5e1; background:#ffffff; color:#1f2937; font-size:16px; }}
  input::placeholder, textarea::placeholder {{ color:#9aa6b2; }}
  input:focus, textarea:focus, select:focus {{ outline:none; border-color:#1560A8; box-shadow:0 0 0 3px rgba(21,96,168,.15); }}
  textarea {{ min-height:96px; resize:vertical; }}
  button {{ margin-top:22px; width:100%; padding:14px; border:0; border-radius:12px;
    background:#1560A8; color:#ffffff; font-weight:800; font-size:16px; cursor:pointer; }}
  button:hover {{ background:#0f4c85; }}
  button:disabled {{ opacity:.6; cursor:progress; }}
  .ok {{ background:#eaf7ee; border:1px solid #16a34a; color:#166534; padding:14px; border-radius:12px; margin-top:16px; }}
  .err {{ background:#fef2f2; border:1px solid #fecaca; color:#b91c1c; padding:14px; border-radius:12px; margin-top:16px; }}
  img.qr {{ width: 260px; max-width: 80%; height:auto; background:#fff; border-radius:12px; padding:8px; border:1px solid #e5e9f0; }}
  a {{ color:#1560A8; }}
  code {{ background:#f1f5f9; padding:2px 6px; border-radius:6px; }}
  .top {{ display:flex; align-items:flex-start; justify-content:space-between; gap:12px; }}
  .langs {{ display:flex; gap:6px; flex:0 0 auto; }}
  .flag {{ padding:2px; background:transparent; border:0; width:auto; margin:0; cursor:pointer;
           border-radius:999px; line-height:0; opacity:.45; transition:opacity .15s, box-shadow .15s; }}
  .flag:hover {{ opacity:.85; }}
  .flag.active {{ opacity:1; box-shadow:0 0 0 2px #1560A8; }}
  .flag img {{ display:block; border-radius:999px; }}
  .hero {{ text-align:center; padding:20px 0 8px; }}
  /* Logo real de Valentino's (solo la V) recortado en círculo. */
  .logo {{ width:124px; height:124px; margin:8px auto 16px; border-radius:50%;
           background-image:url(/images/brand/valentinos-v.jpg);
           background-repeat:no-repeat; background-size:112%; background-position:50% 46%;
           border:1px solid #e5e9f0; box-shadow:0 10px 26px rgba(15,23,42,.18); }}
  .btn-report {{ display:block; width:100%; box-sizing:border-box; text-decoration:none; text-align:center;
    margin-top:22px; padding:16px; border:0; border-radius:12px; background:#1560A8; color:#ffffff;
    font-weight:800; font-size:17px; cursor:pointer; }}
  .btn-report:hover {{ background:#0f4c85; }}
  a.btn-outline {{ display:block; text-decoration:none; text-align:center; margin-top:12px;
    padding:15px; border-radius:12px; background:transparent; border:1px solid #cbd5e1; color:#334155;
    font-weight:700; font-size:15px; }}
  a.btn-outline:hover {{ border-color:#94a3b8; background:#f1f5f9; }}
  input.ro {{ color:#64748b; }}
  /* Autocompletar personalizado (dropdown propio, confiable en móvil). */
  .ac {{ position:relative; }}
  .ac-list {{ position:absolute; left:0; right:0; top:calc(100% + 4px); z-index:30;
    background:#ffffff; border:1px solid #cbd5e1; border-radius:10px; max-height:230px;
    overflow-y:auto; -webkit-overflow-scrolling:touch; touch-action:pan-y; overscroll-behavior:contain;
    display:none; box-shadow:0 12px 30px rgba(15,23,42,.15); }}
  .ac-list.open {{ display:block; }}
  .ac-item {{ padding:12px 14px; cursor:pointer; font-size:15px; color:#334155;
    border-bottom:1px solid #eef2f6; }}
  .ac-item:last-child {{ border-bottom:0; }}
  .ac-item.active, .ac-item:hover {{ background:#eef4fb; color:#0f4c85; }}
  .ac-empty {{ padding:12px 14px; color:#94a3b8; font-size:14px; }}
  /* Selector Operational / A problem en una sola línea (radios excluyentes). */
  .segbar {{ display:flex; gap:10px; }}
  .seg {{ flex:1; display:flex; align-items:center; justify-content:center; gap:8px; cursor:pointer;
    border:1px solid #cbd5e1; border-radius:10px; padding:12px; font-size:15px; font-weight:600;
    color:#334155; transition:border-color .15s, background .15s; }}
  .seg input {{ width:auto; margin:0; accent-color:#1560A8; }}
  .seg:has(input:checked) {{ border-color:#1560A8; background:#eef4fb; color:#0f4c85; }}
  /* Estados apilados (cajas separadas), poco espacio vertical entre ellos. */
  .statuslist {{ display:flex; flex-direction:column; gap:8px; }}
  .stat {{ display:flex; align-items:center; gap:12px; margin:0; cursor:pointer; font-size:15px; font-weight:600;
    color:#334155; border:1px solid #cbd5e1; border-radius:10px; padding:12px 14px; }}
  .stat input {{ width:auto; margin:0; accent-color:#1560A8; flex:0 0 auto; }}
  .stat span {{ flex:1; }}
  .stat .dot {{ width:18px; height:18px; border-radius:50%; flex:0 0 auto;
    box-shadow:0 0 0 3px rgba(15,23,42,.05); }}
  .dot-green {{ background:#16a34a; }}
  .dot-amber {{ background:#f59e0b; }}
  .dot-red {{ background:#ef4444; }}
  .dot-gray {{ background:#94a3b8; }}
  .stat:has(input:checked) {{ border-color:#1560A8; background:#eef4fb; }}
  /* Confirmación de envío estilo Material: onda verde + check animado. */
  .success {{ text-align:center; padding:28px 0 12px; }}
  .ck {{ position:relative; width:100px; height:100px; margin:0 auto 16px; border-radius:50%;
    background:#22c55e; display:flex; align-items:center; justify-content:center;
    box-shadow:0 10px 30px rgba(34,197,94,.35); animation: ck-pop .45s cubic-bezier(.2,.85,.3,1.25) both; }}
  .ck-svg {{ width:52px; height:52px; position:relative; z-index:2; }}
  .ck-check {{ fill:none; stroke:#ffffff; stroke-width:3; stroke-linecap:round; stroke-linejoin:round;
    stroke-dasharray:26; stroke-dashoffset:26; animation: ck-draw .4s ease-out .45s forwards; }}
  .wave {{ position:absolute; inset:0; border-radius:50%; background:#22c55e; opacity:.35; z-index:0;
    animation: ck-wave 1s ease-out .15s both; }}
  @keyframes ck-pop {{ from {{ transform:scale(0); }} to {{ transform:scale(1); }} }}
  @keyframes ck-draw {{ to {{ stroke-dashoffset:0; }} }}
  @keyframes ck-wave {{ from {{ transform:scale(.6); opacity:.5; }} to {{ transform:scale(2.3); opacity:0; }} }}
  .msg-ok {{ color:#334155; font-weight:600; font-size:16px; max-width:340px; margin:0 auto; }}
  .success .badge {{ margin-top:16px; }}
  /* Cuando el reporte/estado se completa, el contenido se centra verticalmente. */
  .card.done {{ justify-content:center; }}
  @media (prefers-reduced-motion: reduce) {{
    .ck, .ck-check, .wave {{ animation:none; }}
    .ck-check {{ stroke-dashoffset:0; }}
  }}
</style>
</head>
<body><div class=""wrap {wrapClass}"">{bodyInner}</div>
<script>
  // Confirmación compartida: check animado centrado + mensaje + chip del activo.
  function vSuccess(text) {{
    var badge = document.querySelector('.badge');
    var chip = badge ? badge.textContent : '';
    var card = document.querySelector('.card');
    if (card) card.classList.add('done');
    ['.top', '.muted', '.hero', '#f'].forEach(function (s) {{
      var el = document.querySelector(s); if (el) el.style.display = 'none';
    }});
    if (badge) badge.style.display = 'none';
    document.querySelectorAll('.btn-report, .btn-outline').forEach(function (el) {{ el.style.display = 'none'; }});
    var msg = document.getElementById('msg');
    if (!msg) return;
    msg.innerHTML =
      '<div class=""success"">'
      + '<div class=""ck""><span class=""wave""></span>'
      + '<svg class=""ck-svg"" viewBox=""0 0 24 24"">'
      + '<path class=""ck-check"" d=""M4 12.5l5 5 11-11""/>'
      + '</svg></div>'
      + '<div class=""msg-ok"">' + text + '</div>'
      + (chip ? '<div class=""badge"">' + chip + '</div>' : '')
      + '</div>';
  }}
</script>
</body>
</html>";

    private static string FormHtml(string tenantNombre, string slug, string codigo, string tipoNombre)
    {
        // Contexto HTML: HtmlEncode. Contexto JS: JsonSerializer + encodeURIComponent.
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var tipoH = WebUtility.HtmlEncode(tipoNombre);
        var codigoH = WebUtility.HtmlEncode(codigo);
        var slugJs = JsonSerializer.Serialize(slug);
        var codigoJs = JsonSerializer.Serialize(codigo);

        var inner =
$@"<div class=""card"">
  <div class=""top"">
    <h1 data-i18n=""title"">Report</h1>
    <div class=""langs"">
      <button type=""button"" class=""flag"" id=""flag-en"" title=""English"" onclick=""setLang('en')"">
        <img src=""/images/flags/us-circle.svg"" alt=""English"" width=""26"" height=""26"">
      </button>
      <button type=""button"" class=""flag"" id=""flag-es"" title=""Español"" onclick=""setLang('es')"">
        <img src=""/images/flags/es-circle.svg"" alt=""Español"" width=""26"" height=""26"">
      </button>
    </div>
  </div>
  <div class=""muted"">{tenantH} · <span data-i18n=""sub"">Housekeeping</span></div>
  <div class=""badge"">{tipoH} · {codigoH}</div>

  <form id=""f"">
    <label data-i18n=""name"">Employee *</label>
    <div class=""ac"">
      <input name=""reportadoPor"" id=""emp"" required autocomplete=""off"" data-i18n-ph=""namePh"" placeholder=""Start typing your name…"">
      <div class=""ac-list"" id=""empList""></div>
    </div>

    <label data-i18n=""statusLbl"">Status *</label>
    <div class=""statuslist"">
      <label class=""stat""><input type=""radio"" name=""estado"" value=""operational"" checked onchange=""onStatus()""> <span data-i18n=""stOperativa"">Operational</span> <b class=""dot dot-green""></b></label>
      <label class=""stat""><input type=""radio"" name=""estado"" value=""AMedias"" onchange=""onStatus()""> <span data-i18n=""stFallas"">Working with faults</span> <b class=""dot dot-amber""></b></label>
      <label class=""stat""><input type=""radio"" name=""estado"" value=""NoFunciona"" onchange=""onStatus()""> <span data-i18n=""stFuera"">Out of service</span> <b class=""dot dot-red""></b></label>
      <label class=""stat""><input type=""radio"" name=""estado"" value=""unavailable"" onchange=""onStatus()""> <span data-i18n=""stUnavailable"">No vacuum available</span> <b class=""dot dot-gray""></b></label>
    </div>

    <label data-i18n=""notesLbl"">Notes</label>
    <textarea name=""descripcion"" data-i18n-ph=""notesPh"" placeholder=""Add any details (optional)""></textarea>

    <div id=""problemFields"" style=""display:none"">
      <label data-i18n=""photos"">Photos (optional)</label>
      <input type=""file"" name=""fotos"" accept=""image/*"" multiple>
    </div>

    <button type=""submit"" id=""btn"" data-i18n=""send"">Send</button>
  </form>
  <div id=""msg""></div>
</div>
<script>
  const SLUG = {slugJs};
  const CODE = {codigoJs};
  const I18N = {{
    en: {{ title:'Report', sub:'Housekeeping', name:'Employee *', namePh:'Start typing your name…',
      statusLbl:'Status *', stOperativa:'Operational', stFallas:'Working with faults', stFuera:'Out of service',
      stUnavailable:'No vacuum available',
      notesLbl:'Notes', notesPh:'Add any details (optional)', notesPhReq:'Describe the problem',
      photos:'Photos (optional)', send:'Send', sending:'Sending…',
      okReport:'Report sent! Thank you. The maintenance team has been notified.',
      okOperational:'Thanks! This equipment was marked as operational.',
      okUnavailable:'Thanks! Noted — no vacuum available.',
      fail:""Couldn't send"", net:'Network error: ' }},
    es: {{ title:'Reportar', sub:'Housekeeping', name:'Empleado *', namePh:'Empieza a escribir tu nombre…',
      statusLbl:'Estado *', stOperativa:'Operativa', stFallas:'Funciona con fallas', stFuera:'Fuera de servicio',
      stUnavailable:'Sin aspiradora disponible',
      notesLbl:'Notas', notesPh:'Agrega detalles (opcional)', notesPhReq:'Describe el problema',
      photos:'Fotos (opcional)', send:'Enviar', sending:'Enviando…',
      okReport:'¡Reporte enviado! Gracias. El equipo de mantenimiento fue avisado.',
      okOperational:'¡Gracias! Este equipo se marcó como operativo.',
      okUnavailable:'¡Gracias! Registrado — sin aspiradora disponible.',
      fail:'No se pudo enviar', net:'Error de red: ' }}
  }};
  let LANG = 'en';
  function setLang(l) {{
    LANG = I18N[l] ? l : 'en';
    const d = I18N[LANG];
    document.querySelectorAll('[data-i18n]').forEach(el => {{ const k = el.getAttribute('data-i18n'); if (d[k]) el.textContent = d[k]; }});
    document.querySelectorAll('[data-i18n-ph]').forEach(el => {{ const k = el.getAttribute('data-i18n-ph'); if (d[k]) el.placeholder = d[k]; }});
    document.getElementById('flag-en').classList.toggle('active', LANG==='en');
    document.getElementById('flag-es').classList.toggle('active', LANG==='es');
    document.documentElement.lang = LANG;
    try {{ localStorage.setItem('lang', LANG); }} catch (e) {{}}
    onStatus();
  }}
  function estadoVal() {{ const r = document.querySelector('input[name=estado]:checked'); return r ? r.value : 'operational'; }}
  function isProblem() {{ return estadoVal() !== 'operational'; }}
  function onStatus() {{
    const p = isProblem();
    document.getElementById('problemFields').style.display = p ? 'block' : 'none';
    document.getElementById('f').classList.toggle('op', !p); // modo operativo -> margen de notas
    const ta = document.querySelector('textarea[name=descripcion]');
    ta.required = p;
    ta.placeholder = p ? I18N[LANG].notesPhReq : I18N[LANG].notesPh;
  }}
  let EMPLOYEES = [];
  function acEsc(s) {{ return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;'); }}
  function acRender(filter) {{
    const box = document.getElementById('empList');
    const q = (filter||'').trim().toLowerCase();
    const matches = EMPLOYEES.filter(function (n) {{ return !q || n.toLowerCase().indexOf(q) >= 0; }}).slice(0,60);
    box.innerHTML = matches.length
      ? matches.map(function (n) {{ return '<div class=""ac-item"" data-v=""'+acEsc(n)+'"">'+acEsc(n)+'</div>'; }}).join('')
      : '<div class=""ac-empty"">No matches</div>';
    box.classList.add('open');
  }}
  function acWire() {{
    const inp = document.getElementById('emp');
    const box = document.getElementById('empList');
    if (!inp || !box) return;
    inp.addEventListener('focus', function () {{ acRender(inp.value); }});
    inp.addEventListener('input', function () {{ acRender(inp.value); }});
    // Seleccionar con 'click': el tap real dispara click, pero un swipe (scroll)
    // NO dispara click, así que se puede scrollear la lista sin seleccionar.
    box.addEventListener('click', function (e) {{
      const it = e.target.closest('.ac-item'); if (!it) return;
      inp.value = it.getAttribute('data-v');
      box.classList.remove('open');
      inp.blur();
    }});
    document.addEventListener('click', function (e) {{
      if (!e.target.closest('.ac')) box.classList.remove('open');
    }});
  }}
  async function loadEmployees() {{
    try {{
      const r = await fetch('/api/public/' + encodeURIComponent(SLUG) + '/employees');
      if (r.ok) EMPLOYEES = await r.json();
    }} catch (e) {{}}
    acWire();
  }}

  const f = document.getElementById('f');
  const btn = document.getElementById('btn');
  const msg = document.getElementById('msg');
  f.addEventListener('submit', async (e) => {{
    e.preventDefault();
    const d = I18N[LANG];
    const val = estadoVal();
    const problem = isProblem();
    btn.disabled = true; btn.textContent = d.sending; msg.innerHTML = '';
    try {{
      const fd = new FormData(f);          // reportadoPor, descripcion, fotos, estado (radio)
      fd.append('nota', f.descripcion.value);
      const r = await fetch('/api/public/' + encodeURIComponent(SLUG) + '/assets/' + encodeURIComponent(CODE) + '/operational', {{ method:'POST', body: fd }});
      if (r.ok) {{
        vSuccess(problem ? d.okReport : (val === 'unavailable' ? d.okUnavailable : d.okOperational));
      }} else {{
        const t = await r.text();
        msg.innerHTML = '<div class=""err"">'+d.fail+' ('+r.status+'). '+t+'</div>';
        btn.disabled=false; btn.textContent=d.send;
      }}
    }} catch (err) {{
      msg.innerHTML = '<div class=""err"">'+d.net+err+'</div>';
      btn.disabled=false; btn.textContent=d.send;
    }}
  }});

  loadEmployees();
  let init = 'en';
  try {{ const saved = localStorage.getItem('lang'); if (saved) init = saved; }} catch (e) {{}}
  setLang(init);
  onStatus();
</script>";
        return Layout($"Reportar {codigoH}", inner, "fill");
    }

    private static string IntroHtml(string tenantNombre, string slug, string codigo, string tipoNombre)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var tipoH = WebUtility.HtmlEncode(tipoNombre);
        var codigoH = WebUtility.HtmlEncode(codigo);
        var slugUrl = Uri.EscapeDataString(slug);
        var codigoUrl = Uri.EscapeDataString(codigo);

        var inner =
$@"<div class=""card"">
  <div class=""top"">
    <div></div>
    <div class=""langs"">
      <button type=""button"" class=""flag"" id=""flag-en"" title=""English"" onclick=""setLang('en')"">
        <img src=""/images/flags/us-circle.svg"" alt=""English"" width=""26"" height=""26"">
      </button>
      <button type=""button"" class=""flag"" id=""flag-es"" title=""Español"" onclick=""setLang('es')"">
        <img src=""/images/flags/es-circle.svg"" alt=""Español"" width=""26"" height=""26"">
      </button>
    </div>
  </div>

  <div class=""hero"">
    <div class=""logo"" role=""img"" aria-label=""Valentino's Group""></div>
    <h1 style=""margin:0"">{tenantH}</h1>
    <div class=""muted"" data-i18n=""prompt"">Report this equipment's status</div>
    <div class=""badge"">{tipoH} · {codigoH}</div>
  </div>

  <a class=""btn-report"" href=""/r/{slugUrl}/{codigoUrl}/reportar"" data-i18n=""report"">Report</a>
</div>
<script>
  const I18N = {{
    en: {{ prompt:""Report this equipment's status"", report:'Report' }},
    es: {{ prompt:'Reporta el estado de este equipo', report:'Reportar' }}
  }};
  let LANG = 'en';
  function setLang(l) {{
    LANG = I18N[l] ? l : 'en';
    const d = I18N[LANG];
    document.querySelectorAll('[data-i18n]').forEach(el => {{ const k = el.getAttribute('data-i18n'); if (d[k]) el.textContent = d[k]; }});
    document.getElementById('flag-en').classList.toggle('active', LANG==='en');
    document.getElementById('flag-es').classList.toggle('active', LANG==='es');
    document.documentElement.lang = LANG;
    try {{ localStorage.setItem('lang', LANG); }} catch (e) {{}}
  }}
  let init = 'en';
  try {{ const saved = localStorage.getItem('lang'); if (saved) init = saved; }} catch (e) {{}}
  setLang(init);
</script>";
        return Layout($"{tenantNombre} · {codigo}", inner);
    }

    private static string LandingHtml(string tenantNombre, string slug, List<string> assets, string baseUrl)
    {
        var baseParam = Uri.EscapeDataString(baseUrl);
        var slugUrl = Uri.EscapeDataString(slug);   // segmento de URL
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var sb = new StringBuilder();
        sb.Append($@"<div class=""card""><h1>Demo — {tenantH}</h1>
<div class=""muted"">Escanea el QR con tu celular para abrir el formulario de reporte.</div>");
        foreach (var codigo in assets)
        {
            var codigoUrl = Uri.EscapeDataString(codigo);      // segmento de URL
            var codigoH = WebUtility.HtmlEncode(codigo);       // texto/atributo HTML
            sb.Append($@"<div style=""margin-top:20px;text-align:center"">
  <div class=""badge"">{codigoH}</div><br>
  <img class=""qr"" src=""/api/public/{slugUrl}/assets/{codigoUrl}/qr.png?base={baseParam}"" alt=""QR {codigoH}"">
  <div class=""muted"" style=""margin-top:8px"">
    <a href=""/r/{slugUrl}/{codigoUrl}"">/r/{codigoH}</a>
  </div>
</div>");
        }
        if (assets.Count == 0)
            sb.Append(@"<p class=""muted"">No hay activos sembrados todavía.</p>");
        sb.Append("</div>");
        return Layout($"Demo {tenantH}", sb.ToString());
    }

    private static string NotFoundHtml(string codigo) =>
        Layout("No encontrado",
            $@"<div class=""card""><h1>Activo no encontrado</h1>
<div class=""muted"">No existe un activo con código <code>{WebUtility.HtmlEncode(codigo)}</code> para este cliente.</div></div>");
}
