using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IWebHostEnvironment _env;
    private readonly Valentinos.Application.Abstractions.ITenantContext _tenant;
    private readonly Valentinos.Api.Reports.ReportEmailer _reportEmailer;

    public DemoController(AppDbContext db, IQrRenderer qr, ILogger<DemoController> logger,
        Valentinos.Application.Notifications.IEmailSender email,
        Valentinos.Application.Reports.IReportService reports,
        IWebHostEnvironment env,
        Valentinos.Application.Abstractions.ITenantContext tenant,
        Valentinos.Api.Reports.ReportEmailer reportEmailer)
    {
        _db = db;
        _qr = qr;
        _logger = logger;
        _email = email;
        _reports = reports;
        _env = env;
        _tenant = tenant;
        _reportEmailer = reportEmailer;
    }

    // URLs públicas SIEMPRE llevan el site slug en el path: /{siteSlug}/e/{code}, /{siteSlug}/f/hk.
    private static string EquipmentUrl(string baseUrl, string siteSlug, string codigo) =>
        $"{baseUrl}/{Uri.EscapeDataString(siteSlug)}/e/{Uri.EscapeDataString(codigo)}";

    private static string EquipmentReportPath(string siteSlug, string codigo) =>
        $"/{Uri.EscapeDataString(siteSlug)}/e/{Uri.EscapeDataString(codigo)}/report";

    // URL pública del QR fijo (equipo no disponible) de un site.
    private static string FixedUrl(string baseUrl, string siteSlug) =>
        $"{baseUrl}/{Uri.EscapeDataString(siteSlug)}/f/hk";

    // Resuelve un site por su slug + su tenant.
    private async Task<(Domain.Entities.Tenant? tenant, Domain.Entities.Site? site)> ResolveSiteAsync(string siteSlug)
    {
        var site = await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Slug == siteSlug);
        if (site is null) return (null, null);
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == site.TenantId);
        return (tenant, site);
    }

    // Resuelve site + equipo (vacuum) por (siteSlug, código). Código es único por site.
    private async Task<(Domain.Entities.Tenant? tenant, Domain.Entities.Site? site, Domain.Entities.Asset? asset, Domain.Entities.AssetType? tipo)>
        ResolveEquipmentAsync(string siteSlug, string codigo)
    {
        var (tenant, site) = await ResolveSiteAsync(siteSlug);
        if (tenant is null || site is null) return (tenant, site, null, null);
        var asset = await _db.Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.SiteId == site.Id && a.Codigo == codigo);
        var tipo = asset is null ? null
            : await _db.AssetTypes.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == asset.AssetTypeId);
        return (tenant, site, asset, tipo);
    }

    // Carga el logo de marca (PNG) desde wwwroot para incrustarlo al centro del QR.
    private byte[]? LoadBrandLogo(string? logo)
    {
        if (string.IsNullOrWhiteSpace(logo) || logo is "0" or "none") return null;
        var file = logo is "mastercorp" or "1" or "true" ? "mastercorp-logo.png" : null;
        if (file is null) return null;
        var path = Path.Combine(_env.WebRootPath ?? "wwwroot", "images", "brand", file);
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
    }

    // Check-in de estado "operativo": el housekeeper confirma que el equipo funciona.
    // Se registra en el log y se envía el correo (plantilla operativa, en inglés).
    // Check-in unificado de estado (operativo / con fallas / fuera de servicio / sin
    // aspiradora). Registra el check-in (KPIs) y, si es un problema, crea el Report (fotos).
    [HttpPost("/api/public/{siteSlug}/equipments/{codigo}/operational")]
    public async Task<IActionResult> MarkOperational(string siteSlug, string codigo,
        [FromForm] string? reportadoPor, [FromForm] string? nota, [FromForm] string? estado,
        [FromForm] IFormFileCollection? fotos)
    {
        var (tenant, site, asset, _) = await ResolveEquipmentAsync(siteSlug, codigo);
        if (tenant is null || site is null || asset is null) return NotFound();

        var estadoKey = string.IsNullOrWhiteSpace(estado) ? "operational" : estado;
        var by = string.IsNullOrWhiteSpace(reportadoPor) ? "-" : reportadoPor;

        // Persistir el check-in (alimenta la página de KPIs) con su SiteId.
        _db.StatusCheckins.Add(new Domain.Entities.StatusCheckin
        {
            SiteId = site.Id,
            EmployeeName = by, AssetCodigo = asset.Codigo, EstadoKey = estadoKey, Nota = nota,
            CreatedAt = DateTime.Now // demo: hora local, consistente con el seeder y los KPIs
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
    [HttpGet("api/public/{siteSlug}/equipments/{codigo}/qr.png")]
    public async Task<IActionResult> QrPng(string siteSlug, string codigo, [FromQuery] string? @base)
    {
        var (tenant, site, equipment, _) = await ResolveEquipmentAsync(siteSlug, codigo);
        if (tenant is null || site is null || equipment is null) return NotFound();

        var baseUrl = string.IsNullOrWhiteSpace(@base) ? PublicBaseUrl() : @base.TrimEnd('/');
        var url = EquipmentUrl(baseUrl, siteSlug, codigo);
        var png = _qr.RenderPngForUrl(url, codigo, LoadBrandLogo("mastercorp"));
        return File(png, "image/png");
    }

    // ---------- Generador de QR custom (con logo de MasterCorp al centro) ----------

    // Imagen del QR custom: codifica /e/{slug}/{code} y admite logo de marca al centro.
    // `download=1` fuerza la descarga del PNG.
    [HttpGet("/qr/{siteSlug}/img.png")]
    public IActionResult QrCustomImg(string siteSlug, [FromQuery] string? code,
        [FromQuery] string? logo, [FromQuery] string? @base, [FromQuery] int download = 0)
    {
        var codigo = string.IsNullOrWhiteSpace(code) ? "VAC-001" : code.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(@base) ? PublicBaseUrl() : @base.TrimEnd('/');
        var url = EquipmentUrl(baseUrl, siteSlug, codigo);
        var png = _qr.RenderPngForUrl(url, codigo, LoadBrandLogo(logo ?? "mastercorp"));
        if (download == 1)
            return File(png, "image/png", $"QR-{codigo}.png");
        return File(png, "image/png");
    }

    // QR FIJO del cuarto de housekeeping de un site: codifica /{siteSlug}/f/hk.
    [HttpGet("/qr/{siteSlug}/fixed.png")]
    public IActionResult QrFixedImg(string siteSlug, [FromQuery] string? @base, [FromQuery] int download = 0)
    {
        var baseUrl = string.IsNullOrWhiteSpace(@base) ? PublicBaseUrl() : @base.TrimEnd('/');
        var url = FixedUrl(baseUrl, siteSlug);
        var png = _qr.RenderPngForUrl(url, "HOUSEKEEPING", LoadBrandLogo("mastercorp"));
        if (download == 1)
            return File(png, "image/png", "QR-housekeeping.png");
        return File(png, "image/png");
    }

    private Guid Tid => _tenant.TenantId ?? Guid.Empty;

    // Generador de QR de un SITE (panel admin). Acepta guid o slug.
    // Vista pública (solo lectura): el generador de QR es accesible sin login.
    // La gestión de sites/vacuums sigue protegida en AdminController.
    [AllowAnonymous]
    [HttpGet("/admin/sites/{key}/qr")]
    public async Task<IActionResult> QrGenerator(string key)
    {
        var (tenant, site) = await ResolveSiteInTenantAsync(key);
        if (tenant is null || site is null) return NotFound();

        var codes = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.SiteId == site.Id && a.Estado == Domain.Enums.AssetEstado.Activo)
            .OrderBy(a => a.Codigo).Select(a => a.Codigo).ToListAsync();

        return Content(QrGeneratorHtml(tenant.Nombre, site, codes, PublicBaseUrl()),
            "text/html; charset=utf-8");
    }

    // Hoja PDF imprimible con TODOS los QRs del site (por defecto 6 por página).
    // Parte de la vista pública del generador (descargar la hoja de QRs).
    [AllowAnonymous]
    [HttpGet("/qr/{siteSlug}/sheet.pdf")]
    public async Task<IActionResult> QrSheet(string siteSlug, [FromQuery] string? @base, [FromQuery] int perpage = 6)
    {
        var site = await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Slug == siteSlug && s.TenantId == Tid);
        if (site is null) return NotFound();

        var codes = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.SiteId == site.Id && a.Estado == Domain.Enums.AssetEstado.Activo)
            .OrderBy(a => a.Codigo).Select(a => a.Codigo).ToListAsync();

        var baseUrl = string.IsNullOrWhiteSpace(@base) ? PublicBaseUrl() : @base.TrimEnd('/');
        var logo = LoadBrandLogo("mastercorp");
        var pngs = codes.Select(c => _qr.RenderPngForUrl(EquipmentUrl(baseUrl, siteSlug, c), c, logo)).ToList();

        var pdf = Qr.QrSheetPdf.Render(pngs, perpage);
        return File(pdf, "application/pdf");
    }

    // Resuelve un site por clave (guid o slug) validando que pertenezca al tenant (admin).
    private async Task<(Domain.Entities.Tenant? tenant, Domain.Entities.Site? site)> ResolveSiteInTenantAsync(string key)
    {
        var site = Guid.TryParse(key, out var gid)
            ? await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == gid && s.TenantId == Tid)
            : await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Slug == key && s.TenantId == Tid);
        if (site is null) return (null, null);
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        return (tenant, site);
    }

    // Página del equipo que abre el QR: logo + botón para reportar. Ruta por site.
    [HttpGet("/{siteSlug}/e/{codigo}")]
    public async Task<IActionResult> Intro(string siteSlug, string codigo)
    {
        var (tenant, site, equipment, tipo) = await ResolveEquipmentAsync(siteSlug, codigo);
        if (tenant is null || site is null) return NotFound();
        if (equipment is null) return Content(NotFoundHtml(codigo), "text/html; charset=utf-8");

        return Content(IntroHtml(tenant.Nombre, EquipmentReportPath(siteSlug, codigo), codigo, tipo?.Nombre ?? "Equipment"),
            "text/html; charset=utf-8");
    }

    // Formulario de reporte del equipo (se llega desde el botón de la página del equipo).
    [HttpGet("/{siteSlug}/e/{codigo}/report")]
    public async Task<IActionResult> ReportForm(string siteSlug, string codigo)
    {
        var (tenant, site, equipment, tipo) = await ResolveEquipmentAsync(siteSlug, codigo);
        if (tenant is null || site is null) return NotFound();
        if (equipment is null) return Content(NotFoundHtml(codigo), "text/html; charset=utf-8");

        return Content(FormHtml(tenant.Nombre, siteSlug, codigo, tipo?.Nombre ?? "Equipment"),
            "text/html; charset=utf-8");
    }

    // ---------- Flujo de "equipo no disponible" (QR fijo del cuarto de housekeeping) ----------

    // QR fijo del cuarto de housekeeping de un site: /{siteSlug}/f/hk.
    [HttpGet("/{siteSlug}/f/hk")]
    public async Task<IActionResult> UnavailableIntroFixed(string siteSlug)
    {
        var (tenant, site) = await ResolveSiteAsync(siteSlug);
        if (tenant is null || site is null) return NotFound();
        return Content(UnavailableIntroHtml(tenant.Nombre, siteSlug), "text/html; charset=utf-8");
    }

    // Formulario de "equipo no disponible" del site: tipo (Vacuum), empleado, notas.
    [HttpGet("/{siteSlug}/f/report")]
    public async Task<IActionResult> UnavailableForm(string siteSlug)
    {
        var (tenant, site) = await ResolveSiteAsync(siteSlug);
        if (tenant is null || site is null) return NotFound();
        var tipos = await _db.AssetTypes.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenant.Id).OrderBy(t => t.Nombre).Select(t => t.Nombre).ToListAsync();
        if (tipos.Count == 0) tipos.Add("Vacuum");
        return Content(UnavailableFormHtml(tenant.Nombre, siteSlug, tipos), "text/html; charset=utf-8");
    }

    // ---------- Compatibilidad con QR impresos ANTES de los sites (sin slug) ----------
    // Los QR del site 069 ya se imprimieron y pegaron apuntando al formato viejo:
    //   por equipo:  {base}/e/{codigo}   ·   QR fijo housekeeping: {base}/f/hk
    // (el tenant se infiere del subdominio; no llevaban slug de site). Estas rutas
    // resuelven al site correspondiente del tenant para que esos QR sigan sirviendo
    // sin reimprimir. Los enlaces internos (form, POST) que sirve el HTML ya usan el
    // formato actual con slug, así que solo hacen falta estos dos entry points.

    // Site "por defecto" del tenant = el más antiguo (el original, p. ej. 069).
    private Task<Domain.Entities.Site?> DefaultSiteAsync() =>
        _db.Sites.IgnoreQueryFilters()
            .Where(s => s.TenantId == Tid)
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync();

    // QR viejo por equipo: /e/{codigo} (sin slug). El código es único por site y los
    // legacy sólo existen en el site original; se resuelve por (tenant, código).
    [HttpGet("/e/{codigo}")]
    public async Task<IActionResult> IntroLegacy(string codigo)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        if (tenant is null) return NotFound();
        var asset = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenant.Id && a.Codigo == codigo)
            .OrderBy(a => a.CreatedAt).FirstOrDefaultAsync();
        if (asset is null) return Content(NotFoundHtml(codigo), "text/html; charset=utf-8");
        var site = await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == asset.SiteId);
        if (site is null) return NotFound();
        var tipo = await _db.AssetTypes.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == asset.AssetTypeId);
        return Content(IntroHtml(tenant.Nombre, EquipmentReportPath(site.Slug, codigo), codigo, tipo?.Nombre ?? "Equipment"),
            "text/html; charset=utf-8");
    }

    // QR fijo viejo del cuarto de housekeeping: /f/hk (sin slug) → site por defecto.
    [HttpGet("/f/hk")]
    public async Task<IActionResult> UnavailableIntroLegacy()
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        if (tenant is null) return NotFound();
        var site = await DefaultSiteAsync();
        if (site is null) return NotFound();
        return Content(UnavailableIntroHtml(tenant.Nombre, site.Slug), "text/html; charset=utf-8");
    }

    // Registra un reporte de equipo no disponible (con su SiteId).
    [HttpPost("/api/public/{siteSlug}/unavailable")]
    public async Task<IActionResult> ReportUnavailable(string siteSlug,
        [FromForm] string? reportadoPor, [FromForm] string? tipoEquipo, [FromForm] string? nota)
    {
        var (tenant, site) = await ResolveSiteAsync(siteSlug);
        if (tenant is null || site is null) return NotFound();

        var by = string.IsNullOrWhiteSpace(reportadoPor) ? "-" : reportadoPor!.Trim();
        var tipo = string.IsNullOrWhiteSpace(tipoEquipo) ? "Vacuum" : tipoEquipo!.Trim();

        _db.UnavailableReports.Add(new Domain.Entities.UnavailableReport
        {
            SiteId = site.Id,
            EmployeeName = by, EquipmentType = tipo, Nota = string.IsNullOrWhiteSpace(nota) ? null : nota,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("🚫 NO DISPONIBLE: {Tipo} ({Tenant}/{Site}) por {Por}",
            tipo, tenant.Nombre, site.Code, by);
        return Ok(new { ok = true });
    }

    // Lista de empleados DEL SITE (para el autocompletar del formulario).
    [HttpGet("/api/public/{siteSlug}/employees")]
    public async Task<IActionResult> Employees(string siteSlug)
    {
        var (tenant, site) = await ResolveSiteAsync(siteSlug);
        if (tenant is null || site is null) return NotFound();

        var names = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenant.Id && e.SiteId == site.Id)
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

    // ---------- Dashboard de KPIs por SITE (panel admin) ----------
    private async Task<(Domain.Entities.Tenant? tenant, Domain.Entities.Site? site, List<Kpi.KpiCheckin> checkins, List<Kpi.KpiUnavailable> unavailable)> LoadSiteCheckinsAsync(string key)
    {
        var (_, site) = await ResolveSiteInTenantAsync(key);
        if (site is null) return (null, null, new(), new());
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        var since = DateTime.Now.Date.AddDays(-29);
        var list = await _db.StatusCheckins.IgnoreQueryFilters()
            .Where(c => c.SiteId == site.Id && c.CreatedAt >= since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new Kpi.KpiCheckin(c.EmployeeName, c.AssetCodigo, c.EstadoKey, c.Nota, c.CreatedAt))
            .ToListAsync();
        var unav = await _db.UnavailableReports.IgnoreQueryFilters()
            .Where(u => u.SiteId == site.Id && u.CreatedAt >= since)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new Kpi.KpiUnavailable(u.EmployeeName, u.Nota, u.CreatedAt))
            .ToListAsync();
        return (tenant, site, list, unav);
    }

    // Resuelve la "key" del site: el ?site= del query o, si falta, el site por defecto.
    private async Task<string?> ReportsSiteKeyAsync(string? site)
    {
        if (!string.IsNullOrWhiteSpace(site)) return site;
        var def = await DefaultSiteAsync();
        return def?.Slug;
    }

    // Vista pública (solo lectura): dashboard de KPIs en /reports?site={slugOrGuid}.
    // Si no se pasa site, usa el site por defecto. La página trae un dropdown con
    // los sites disponibles del tenant (el actual queda seleccionado).
    [AllowAnonymous]
    [HttpGet("/reports")]
    public async Task<IActionResult> KpiPage([FromQuery] string? site, [FromQuery] string? period)
    {
        var key = await ReportsSiteKeyAsync(site);
        if (string.IsNullOrWhiteSpace(key)) return NotFound();
        var (tenant, s, list, unav) = await LoadSiteCheckinsAsync(key);
        if (tenant is null || s is null) return NotFound();
        var sites = await _db.Sites.IgnoreQueryFilters()
            .Where(x => x.TenantId == Tid)
            .OrderByDescending(x => x.Code)
            .Select(x => new Kpi.KpiSiteOption(x.Slug, x.Code))
            .ToListAsync();
        var model = Kpi.Kpi.Compute($"{tenant.Nombre} · Site {s.Code}", list, unav, DateTime.Now, period ?? "daily");
        return Content(Kpi.KpiHtml.Render(model, s.Slug, sites), "text/html; charset=utf-8");
    }

    // Envía un SAMPLE del reporte por correo (mismo formato/lógica que el scheduler),
    // pero SOLO a los destinatarios explícitos (default: pedroyarleque96@gmail.com),
    // sin tocar los destinatarios de la web (NotificationEmails) ni el CC configurado.
    // El reporte es a nivel de TENANT (combina los 4 sites). Admite ?period= y ?asof=yyyy-MM-dd
    // (asof simula el "hoy" del cálculo, útil para ver un día con actividad).
    [Authorize(Roles = "admin")]
    [HttpPost("/admin/reports/sample")]
    public async Task<IActionResult> SendSampleReport([FromQuery] string? to, [FromQuery] string? period, [FromQuery] string? asof)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        if (tenant is null) return NotFound();
        var dest = string.IsNullOrWhiteSpace(to) ? "pedroyarleque96@gmail.com" : to.Trim();
        DateTime? asOf = DateTime.TryParse(asof, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d.Date.AddHours(23).AddMinutes(59) : null;
        var sent = await _reportEmailer.SendAsync(tenant, period ?? "daily",
            toOverride: new[] { dest }, includeCc: false, asOf: asOf);
        var msg = sent.Count > 0
            ? $"OK: sample '{period ?? "daily"}' enviado SOLO a {string.Join(", ", sent)} (sin CC, sin destinatarios de la web)."
            : "No se envió (SMTP apagado o sin destinatarios).";
        return Content(msg, "text/plain; charset=utf-8");
    }

    // Parte de la vista pública de reportes (descargar el PDF): /reports/pdf?site={key}.
    [AllowAnonymous]
    [HttpGet("/reports/pdf")]
    public async Task<IActionResult> KpiPdfPreview([FromQuery] string? site, [FromQuery] string? period)
    {
        var key = await ReportsSiteKeyAsync(site);
        if (string.IsNullOrWhiteSpace(key)) return NotFound();
        var (tenant, s, list, unav) = await LoadSiteCheckinsAsync(key);
        if (tenant is null || s is null) return NotFound();
        var model = Kpi.Kpi.Compute($"{tenant.Nombre} · Site {s.Code}", list, unav, DateTime.Now, period ?? "daily");
        return File(Kpi.KpiPdf.Render(model), "application/pdf");
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
<link rel=""icon"" href=""/favicon.ico"" sizes=""any"">
<link rel=""icon"" type=""image/png"" href=""/favicon-32.png"">
<link rel=""apple-touch-icon"" href=""/favicon-180.png"">
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
      const r = await fetch('/api/public/' + encodeURIComponent(SLUG) + '/equipments/' + encodeURIComponent(CODE) + '/operational', {{ method:'POST', body: fd }});
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

    private static string IntroHtml(string tenantNombre, string reportHref, string codigo, string tipoNombre)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var tipoH = WebUtility.HtmlEncode(tipoNombre);
        var codigoH = WebUtility.HtmlEncode(codigo);
        var reportHrefH = WebUtility.HtmlEncode(reportHref);

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

  <a class=""btn-report"" href=""{reportHrefH}"" data-i18n=""report"">Report</a>
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

    // Pantalla inicial del QR fijo (equipo no disponible). Sin activo; el botón dice "Reportar".
    private static string UnavailableIntroHtml(string tenantNombre, string slug)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var slugUrl = Uri.EscapeDataString(slug);

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
    <div class=""muted"" data-i18n=""prompt"">Report equipment that isn't available</div>
  </div>

  <a class=""btn-report"" href=""/{slugUrl}/f/report"" data-i18n=""report"">Report</a>
</div>
<script>
  const I18N = {{
    en: {{ prompt:""Report equipment that isn't available"", report:'Report' }},
    es: {{ prompt:'Reporta un equipo que no está disponible', report:'Reportar' }}
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
        return Layout($"{tenantNombre} · Housekeeping", inner);
    }

    // Formulario de "equipo no disponible": tipo de equipo (preseleccionado), empleado,
    // comentarios. Sin fotos ni estado.
    private static string UnavailableFormHtml(string tenantNombre, string slug, List<string> tipos)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var slugJs = JsonSerializer.Serialize(slug);
        var opts = new StringBuilder();
        for (var i = 0; i < tipos.Count; i++)
            opts.Append($@"<option value=""{WebUtility.HtmlEncode(tipos[i])}""{(i == 0 ? " selected" : "")}>{WebUtility.HtmlEncode(tipos[i])}</option>");

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
  <div class=""badge"" data-i18n=""tag"">Unavailable equipment</div>

  <form id=""f"">
    <label data-i18n=""name"">Employee *</label>
    <div class=""ac"">
      <input name=""reportadoPor"" id=""emp"" required autocomplete=""off"" data-i18n-ph=""namePh"" placeholder=""Start typing your name…"">
      <div class=""ac-list"" id=""empList""></div>
    </div>

    <label data-i18n=""typeLbl"">Equipment type *</label>
    <select name=""tipoEquipo"" id=""tipo"">{opts}</select>

    <label data-i18n=""notesLbl"">Comments</label>
    <textarea name=""nota"" data-i18n-ph=""notesPh"" placeholder=""Add any details (optional)""></textarea>

    <button type=""submit"" id=""btn"" data-i18n=""send"">Send</button>
  </form>
  <div id=""msg""></div>
</div>
<script>
  const SLUG = {slugJs};
  const I18N = {{
    en: {{ title:'Report', sub:'Housekeeping', tag:'Unavailable equipment', name:'Employee *', namePh:'Start typing your name…',
      typeLbl:'Equipment type *', notesLbl:'Comments', notesPh:'Add any details (optional)',
      send:'Send', sending:'Sending…',
      ok:'Report sent! Thanks — the team has been notified.',
      fail:""Couldn't send"", net:'Network error: ' }},
    es: {{ title:'Reportar', sub:'Housekeeping', tag:'Equipo no disponible', name:'Empleado *', namePh:'Empieza a escribir tu nombre…',
      typeLbl:'Tipo de equipo *', notesLbl:'Comentarios', notesPh:'Agrega detalles (opcional)',
      send:'Enviar', sending:'Enviando…',
      ok:'¡Reporte enviado! Gracias — el equipo fue avisado.',
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
    btn.disabled = true; btn.textContent = d.sending; msg.innerHTML = '';
    try {{
      const fd = new FormData(f); // reportadoPor, tipoEquipo, nota
      const r = await fetch('/api/public/' + encodeURIComponent(SLUG) + '/unavailable', {{ method:'POST', body: fd }});
      if (r.ok) {{
        vSuccess(d.ok);
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
</script>";
        return Layout($"{tenantNombre} · Housekeeping", inner, "fill");
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
  <img class=""qr"" src=""/api/public/{slugUrl}/equipments/{codigoUrl}/qr.png?base={baseParam}"" alt=""QR {codigoH}"">
  <div class=""muted"" style=""margin-top:8px"">
    <a href=""/e/{slugUrl}/{codigoUrl}"">/e/{codigoH}</a>
  </div>
</div>");
        }
        if (assets.Count == 0)
            sb.Append(@"<p class=""muted"">No hay equipos sembrados todavía.</p>");
        sb.Append("</div>");
        return Layout($"Demo {tenantH}", sb.ToString());
    }

    // Generador de QR custom de un SITE (con logo de MasterCorp al centro).
    private static string QrGeneratorHtml(string tenantNombre, Domain.Entities.Site site, List<string> assets, string baseUrl)
    {
        var slug = site.Slug;
        var tenantH = WebUtility.HtmlEncode($"{tenantNombre} · Site {site.Code}");
        var slugJs = JsonSerializer.Serialize(slug);
        var baseJs = JsonSerializer.Serialize(baseUrl);
        var slugUrl = Uri.EscapeDataString(slug);
        var baseParam = Uri.EscapeDataString(baseUrl);
        var baseH = WebUtility.HtmlEncode(baseUrl);
        var fixedUrlH = WebUtility.HtmlEncode(FixedUrl(baseUrl, slug));
        var codesJs = JsonSerializer.Serialize(assets);
        var hasVacJs = assets.Count > 0 ? "true" : "false"; // sin vacuums: imprimir muestra modal
        var first = assets.Count > 0 ? WebUtility.HtmlEncode(assets[0]) : "VAC-001";
        const string clip = @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><rect x=""9"" y=""9"" width=""13"" height=""13"" rx=""2""></rect><path d=""M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1""></path></svg>";
        const string dlIco = @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><path d=""M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4""></path><polyline points=""7 10 12 15 17 10""></polyline><line x1=""12"" y1=""15"" x2=""12"" y2=""3""></line></svg>";

        return $@"<!doctype html>
<html lang=""es""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>QR Generator · {tenantH}</title>
<link rel=""icon"" href=""/favicon.ico"" sizes=""any"">
<link rel=""icon"" type=""image/png"" href=""/favicon-32.png"">
<link rel=""apple-touch-icon"" href=""/favicon-180.png"">
<style>
  :root {{ color-scheme: light; }} * {{ box-sizing:border-box; }}
  body {{ margin:0; background:#eef1f5; color:#334155; font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; -webkit-text-size-adjust:100%; }}
  .wrap {{ max-width:560px; margin:0 auto; padding:16px 14px 60px; }}
  .card {{ background:#fff; border:1px solid #e5e9f0; border-radius:16px; padding:18px; box-shadow:0 8px 24px rgba(15,23,42,.05); margin-bottom:14px; }}
  .head {{ display:flex; align-items:center; gap:12px; }}
  .logo {{ width:46px; height:46px; border-radius:12px; flex:0 0 auto; background:#fff url(/images/brand/mastercorp-logo.png) no-repeat 50%/contain; border:1px solid #e5e9f0; }}
  h1 {{ font-size:18px; margin:0; color:#1560A8; }}
  .sub {{ color:#64748b; font-size:12px; margin-top:2px; }}
  label {{ display:block; font-size:13px; font-weight:600; color:#475569; margin:14px 0 6px; }}
  input[type=text] {{ width:100%; padding:12px; border-radius:10px; border:1px solid #cbd5e1; font-size:16px; color:#1f2937; }}
  .chk {{ display:flex; align-items:center; gap:10px; margin-top:14px; font-size:14px; font-weight:600; color:#334155; cursor:pointer; }}
  .chk input {{ width:20px; height:20px; accent-color:#1560A8; }}
  .qrbox {{ text-align:center; }}
  .qrframe {{ position:relative; display:inline-block; padding:14px; background:#fff; border:1px solid #e5e9f0; border-radius:14px; }}
  .qr-dl {{ position:absolute; top:10px; right:10px; width:36px; height:36px; display:inline-flex; align-items:center;
    justify-content:center; border:1px solid #e5e9f0; border-radius:9px; background:rgba(255,255,255,.92); color:#1560A8;
    cursor:pointer; box-shadow:0 2px 8px rgba(15,23,42,.12); transition:.15s; }}
  .qr-dl:hover {{ background:#eef4fb; }}
  .qr-dl.ok {{ color:#16a34a; border-color:#16a34a; background:#eaf7ee; }}
  .qr-dl svg {{ width:18px; height:18px; }}
  .qrframe img {{ display:block; width:260px; height:auto; max-width:100%; }}
  .urlbox {{ margin-top:12px; font-size:12.5px; color:#64748b; word-break:break-all; background:#f8fafc; border:1px solid #e5e9f0; border-radius:10px; padding:10px 12px; }}
  .urlrow {{ display:flex; align-items:stretch; gap:8px; margin-top:12px; }}
  .urlrow .urlbox {{ margin-top:0; flex:1 1 auto; min-width:0; }}
  .copybtn {{ flex:0 0 auto; display:inline-flex; align-items:center; justify-content:center; width:44px;
    border:1px solid #cbd5e1; border-radius:10px; background:#fff; color:#1560A8; cursor:pointer; transition:.15s; }}
  .copybtn:hover {{ background:#eef4fb; }}
  .copybtn.ok {{ color:#16a34a; border-color:#16a34a; background:#eaf7ee; }}
  .copybtn svg {{ width:18px; height:18px; }}
  .btn {{ display:block; width:100%; text-align:center; text-decoration:none; background:#1560A8; color:#fff; border:0; border-radius:10px; padding:13px 16px; font-weight:700; font-size:15px; cursor:pointer; margin-top:14px; }}
  .btn:hover {{ background:#0f4c85; }}
  .top {{ display:flex; align-items:flex-start; justify-content:space-between; gap:12px; }}
  .langs {{ display:flex; gap:6px; flex:0 0 auto; }}
  .flag {{ padding:2px; background:transparent; border:0; cursor:pointer; border-radius:999px; line-height:0; opacity:.45; transition:opacity .15s, box-shadow .15s; }}
  .flag:hover {{ opacity:.85; }}
  .flag.active {{ opacity:1; box-shadow:0 0 0 2px #1560A8; }}
  .flag img {{ display:block; border-radius:999px; width:24px; height:24px; }}
  .ac {{ position:relative; }}
  .ac-list {{ position:absolute; left:0; right:0; top:calc(100% + 4px); z-index:30;
    background:#fff; border:1px solid #cbd5e1; border-radius:10px; max-height:230px;
    overflow-y:auto; -webkit-overflow-scrolling:touch; touch-action:pan-y; overscroll-behavior:contain;
    display:none; box-shadow:0 12px 30px rgba(15,23,42,.15); }}
  .ac-list.open {{ display:block; }}
  .ac-item {{ padding:12px 14px; cursor:pointer; font-size:15px; color:#334155; border-bottom:1px solid #eef2f6; }}
  .ac-item:last-child {{ border-bottom:0; }}
  .ac-item.active, .ac-item:hover {{ background:#eef4fb; color:#0f4c85; }}
  .ac-empty {{ padding:12px 14px; color:#94a3b8; font-size:14px; }}
  .modal-bg {{ position:fixed; inset:0; background:rgba(15,31,48,.45); display:none; align-items:center; justify-content:center; z-index:50; padding:20px; }}
  .modal-bg.show {{ display:flex; }}
  .modal {{ background:#fff; border-radius:16px; max-width:360px; width:100%; padding:24px; text-align:center; box-shadow:0 20px 60px rgba(0,0,0,.25); }}
  .modal .ic {{ font-size:38px; line-height:1; }}
  .modal h3 {{ margin:10px 0 6px; color:#1560A8; font-size:18px; }}
  .modal p {{ margin:0 0 18px; color:#64748b; font-size:14px; }}
</style></head>
<body><div class=""wrap"">
  <div class=""card"">
    <div class=""top"">
      <div class=""head"">
        <div class=""logo""></div>
        <div><h1 data-i18n=""genTitle"">QR Generator</h1><div class=""sub"">{tenantH} · Housekeeping</div></div>
      </div>
      <div class=""langs"">
        <button type=""button"" class=""flag"" id=""flag-en"" title=""English"" onclick=""setLang('en')""><img src=""/images/flags/us-circle.svg"" alt=""English""></button>
        <button type=""button"" class=""flag"" id=""flag-es"" title=""Español"" onclick=""setLang('es')""><img src=""/images/flags/es-circle.svg"" alt=""Español""></button>
      </div>
    </div>
    <label for=""code"" data-i18n=""codeLbl"">Equipment code</label>
    <div class=""ac"">
      <input type=""text"" id=""code"" value=""{first}"" autocomplete=""off"" placeholder=""VAC-001"">
      <div class=""ac-list"" id=""codeList""></div>
    </div>
    <label class=""chk""><input type=""checkbox"" id=""logo"" checked> <span data-i18n=""logoLbl"">Include logo in the center</span></label>
    <a class=""btn"" href=""/qr/{slugUrl}/sheet.pdf?base={baseParam}&amp;perpage=6"" data-i18n=""dlSheet"" target=""_blank"" rel=""noopener"" onclick=""return printGuard(event)"">🖨 Print all QRs (PDF)</a>
  </div>

  <div class=""card qrbox"">
    <div class=""qrframe""><img id=""qr"" alt=""QR"" src=""""><button type=""button"" class=""qr-dl"" id=""saveQr"" title=""Save"" aria-label=""Save"">{dlIco}</button></div>
    <div class=""urlrow"">
      <div class=""urlbox"" id=""url""></div>
      <button type=""button"" class=""copybtn"" id=""copyUrl"" title=""Copy"" aria-label=""Copy"">{clip}</button>
    </div>
    <a class=""btn"" id=""dl"" data-i18n=""dlBtn"" download>⬇ Download PNG</a>
  </div>

  <div class=""card qrbox"">
    <h1 style=""font-size:15px;margin:0 0 4px"" data-i18n=""fixedTitle"">Fixed QR · Housekeeping room</h1>
    <div class=""sub"" style=""margin-bottom:12px"" data-i18n=""fixedDesc"">To report unavailable equipment (without scanning a specific one).</div>
    <div class=""qrframe""><img alt=""QR fijo housekeeping"" src=""/qr/{slugUrl}/fixed.png?base={baseParam}""><button type=""button"" class=""qr-dl"" id=""saveFixed"" title=""Save"" aria-label=""Save"">{dlIco}</button></div>
    <div class=""urlrow"">
      <div class=""urlbox"" id=""urlFixed"">{fixedUrlH}</div>
      <button type=""button"" class=""copybtn"" id=""copyFixed"" title=""Copy"" aria-label=""Copy"">{clip}</button>
    </div>
    <a class=""btn"" href=""/qr/{slugUrl}/fixed.png?base={baseParam}&amp;download=1"" data-i18n=""dlFixed"" download>⬇ Download fixed QR</a>
  </div>
</div>

<div class=""modal-bg"" id=""noVacModal"" onclick=""if(event.target===this)closeNoVac()"">
  <div class=""modal"">
    <div class=""ic"">📭</div>
    <h3 data-i18n=""nvTitle"">No data available</h3>
    <p data-i18n=""nvMsg"">This site has no vacuums yet, so there's nothing to print.</p>
    <button class=""btn"" type=""button"" onclick=""closeNoVac()"" data-i18n=""nvClose"">Close</button>
  </div>
</div>
<script>
  const SLUG = {slugJs}, BASE = {baseJs};
  const HAS_VAC = {hasVacJs};
  function printGuard(e){{ if(!HAS_VAC){{ e.preventDefault(); document.getElementById('noVacModal').classList.add('show'); return false; }} return true; }}
  function closeNoVac(){{ document.getElementById('noVacModal').classList.remove('show'); }}
  const I18N = {{
    en: {{ genTitle:'QR Generator', codeLbl:'Equipment code', logoLbl:'Include logo in the center',
      dlSheet:'🖨 Print all QRs (PDF)', dlBtn:'⬇ Download PNG', fixedTitle:'Fixed QR · Housekeeping room',
      fixedDesc:'To report unavailable equipment (without scanning a specific one).', dlFixed:'⬇ Download fixed QR',
      nvTitle:'No data available', nvMsg:""This site has no vacuums yet, so there's nothing to print."", nvClose:'Close' }},
    es: {{ genTitle:'Generador de QR', codeLbl:'Código del equipo', logoLbl:'Incluir logo al centro',
      dlSheet:'🖨 Imprimir todos los QR (PDF)', dlBtn:'⬇ Descargar PNG', fixedTitle:'QR fijo · Cuarto de housekeeping',
      fixedDesc:'Para reportar un equipo no disponible (sin escanear un equipo específico).', dlFixed:'⬇ Descargar QR fijo',
      nvTitle:'No hay data disponible', nvMsg:'Este site aún no tiene aspiradoras, así que no hay nada para imprimir.', nvClose:'Cerrar' }}
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
  const CODES = {codesJs};
  const codeEl = document.getElementById('code'), logoEl = document.getElementById('logo');
  const qr = document.getElementById('qr'), urlEl = document.getElementById('url'), dl = document.getElementById('dl');
  // Dropdown personalizado de códigos de equipo (mismo estilo que el form).
  function acEsc(s) {{ return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;'); }}
  function acRender(filter) {{
    const box = document.getElementById('codeList');
    const q = (filter||'').trim().toLowerCase();
    const matches = CODES.filter(function (c) {{ return !q || c.toLowerCase().indexOf(q) >= 0; }}).slice(0,60);
    box.innerHTML = matches.length
      ? matches.map(function (c) {{ return '<div class=""ac-item"" data-v=""'+acEsc(c)+'"">'+acEsc(c)+'</div>'; }}).join('')
      : '<div class=""ac-empty"">No matches</div>';
    box.classList.add('open');
  }}
  function acWire() {{
    const box = document.getElementById('codeList');
    // Al enfocar: seleccionar el texto y mostrar TODA la lista (el campo viene precargado).
    codeEl.addEventListener('focus', function () {{ codeEl.select(); acRender(''); }});
    codeEl.addEventListener('input', function () {{ acRender(codeEl.value); }});
    box.addEventListener('click', function (e) {{
      const it = e.target.closest('.ac-item'); if (!it) return;
      codeEl.value = it.getAttribute('data-v');
      box.classList.remove('open');
      codeEl.blur();
      refresh();
    }});
    document.addEventListener('click', function (e) {{ if (!e.target.closest('.ac')) box.classList.remove('open'); }});
  }}
  function imgUrl(dl) {{
    const code = (codeEl.value || 'VAC-001').trim();
    const logo = logoEl.checked ? 'mastercorp' : '0';
    let u = '/qr/' + encodeURIComponent(SLUG) + '/img.png?code=' + encodeURIComponent(code)
          + '&logo=' + logo + '&base=' + encodeURIComponent(BASE);
    if (dl) u += '&download=1';
    return u;
  }}
  function refresh() {{
    const code = (codeEl.value || 'VAC-001').trim();
    qr.src = imgUrl(false);
    dl.href = imgUrl(true);
    urlEl.textContent = BASE + '/' + SLUG + '/e/' + code;
  }}
  codeEl.addEventListener('input', refresh);
  logoEl.addEventListener('change', refresh);
  // Copiar al portapapeles con feedback verde.
  function copyText(text, btn) {{
    function done() {{ btn.classList.add('ok'); setTimeout(function () {{ btn.classList.remove('ok'); }}, 1200); }}
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done).catch(function () {{}});
    else {{ const t = document.createElement('textarea'); t.value = text; document.body.appendChild(t); t.select(); try {{ document.execCommand('copy'); }} catch (e) {{}} document.body.removeChild(t); done(); }}
  }}
  document.getElementById('copyUrl').addEventListener('click', function () {{ copyText(document.getElementById('url').textContent, this); }});
  document.getElementById('copyFixed').addEventListener('click', function () {{ copyText(document.getElementById('urlFixed').textContent, this); }});
  // Detección de móvil: solo ahí usamos el share nativo (→ Guardar en Fotos). En PC, descarga directa.
  const IS_MOBILE = /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent)
    || (navigator.maxTouchPoints > 1 && window.matchMedia('(pointer:coarse)').matches);
  function downloadBlob(blob, filename) {{
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob); a.download = filename;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(function () {{ URL.revokeObjectURL(a.href); }}, 4000);
  }}
  async function saveImage(url, filename, btn) {{
    try {{
      const resp = await fetch(url);
      const blob = await resp.blob();
      if (IS_MOBILE && navigator.canShare) {{
        const file = new File([blob], filename, {{ type: 'image/png' }});
        if (navigator.canShare({{ files: [file] }})) {{ await navigator.share({{ files: [file], title: filename }}); }}
        else {{ downloadBlob(blob, filename); }}
      }} else {{
        downloadBlob(blob, filename); // PC: descarga directa
      }}
      if (btn) {{ btn.classList.add('ok'); setTimeout(function () {{ btn.classList.remove('ok'); }}, 1200); }}
    }} catch (e) {{}}
  }}
  document.getElementById('saveQr').addEventListener('click', function () {{
    const code = (codeEl.value || 'VAC-001').trim();
    saveImage(imgUrl(false), 'QR-' + code + '.png', this);
  }});
  document.getElementById('saveFixed').addEventListener('click', function () {{
    saveImage('/qr/' + encodeURIComponent(SLUG) + '/fixed.png?base=' + encodeURIComponent(BASE), 'QR-housekeeping.png', this);
  }});
  acWire();
  refresh();
  setLang('en'); // default inglés al cargar
</script>
</body></html>";
    }

    private static string NotFoundHtml(string codigo) =>
        Layout("Not found",
            $@"<div class=""card""><h1>Equipment not found</h1>
<div class=""muted"">There is no equipment with code <code>{WebUtility.HtmlEncode(codigo)}</code> for this client.</div></div>");
}
