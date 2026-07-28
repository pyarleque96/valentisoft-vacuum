using System.Text;
using Microsoft.AspNetCore.Mvc;
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

    public DemoController(AppDbContext db, IQrRenderer qr)
    {
        _db = db;
        _qr = qr;
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
    [HttpGet("/r/{slug}/{codigo}")]
    public async Task<IActionResult> ReportForm(string slug, string codigo)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();

        var asset = await _db.Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.Codigo == codigo);
        if (asset is null) return Content(NotFoundHtml(codigo), "text/html; charset=utf-8");

        var tipo = await _db.AssetTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == asset.AssetTypeId);

        return Content(FormHtml(tenant.Nombre, slug, codigo, tipo?.Nombre ?? "Activo"),
            "text/html; charset=utf-8");
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

    private static string Layout(string title, string bodyInner) =>
$@"<!doctype html>
<html lang=""es"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>{title}</title>
<style>
  :root {{ color-scheme: light dark; }}
  body {{ font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 0;
         background: #0f172a; color: #e2e8f0; }}
  .wrap {{ max-width: 560px; margin: 0 auto; padding: 24px 18px 64px; }}
  .card {{ background: #1e293b; border: 1px solid #334155; border-radius: 16px; padding: 22px; }}
  h1 {{ font-size: 20px; margin: 0 0 4px; }}
  .muted {{ color: #94a3b8; font-size: 14px; }}
  .badge {{ display:inline-block; background:#334155; color:#e2e8f0; border-radius:999px;
           padding:4px 12px; font-weight:600; font-size:14px; margin-top:8px; }}
  label {{ display:block; font-size:14px; margin:16px 0 6px; font-weight:600; }}
  input, textarea, select {{ width:100%; box-sizing:border-box; padding:12px; border-radius:10px;
    border:1px solid #475569; background:#0f172a; color:#e2e8f0; font-size:16px; }}
  textarea {{ min-height:96px; resize:vertical; }}
  button {{ margin-top:22px; width:100%; padding:14px; border:0; border-radius:12px;
    background:#22c55e; color:#052e16; font-weight:800; font-size:16px; cursor:pointer; }}
  button:disabled {{ opacity:.6; cursor:progress; }}
  .ok {{ background:#052e16; border:1px solid #16a34a; color:#bbf7d0; padding:14px; border-radius:12px; margin-top:16px; }}
  .err {{ background:#450a0a; border:1px solid #dc2626; color:#fecaca; padding:14px; border-radius:12px; margin-top:16px; }}
  img.qr {{ width: 260px; max-width: 80%; height:auto; background:#fff; border-radius:12px; padding:8px; }}
  a {{ color:#7dd3fc; }}
  code {{ background:#0f172a; padding:2px 6px; border-radius:6px; }}
</style>
</head>
<body><div class=""wrap"">{bodyInner}</div></body>
</html>";

    private static string FormHtml(string tenantNombre, string slug, string codigo, string tipoNombre)
    {
        var inner =
$@"<div class=""card"">
  <h1>Reportar avería</h1>
  <div class=""muted"">{tenantNombre} · Housekeeping</div>
  <div class=""badge"">{tipoNombre} · {codigo}</div>

  <form id=""f"">
    <label>¿Qué le pasa al equipo? *</label>
    <textarea name=""descripcion"" required placeholder=""Ej. No enciende / hace ruido / no aspira""></textarea>

    <label>Severidad *</label>
    <select name=""severidad"" required>
      <option value=""Leve"">Leve</option>
      <option value=""AMedias"">Funciona a medias</option>
      <option value=""NoFunciona"" selected>No funciona</option>
    </select>

    <label>Piso / ubicación</label>
    <input name=""ubicacion"" placeholder=""Ej. Piso 3, habitación 305"">

    <label>Tu nombre (opcional)</label>
    <input name=""reportadoPor"" placeholder=""Ej. Ana"">

    <label>Fotos (opcional)</label>
    <input type=""file"" name=""fotos"" accept=""image/*"" multiple>

    <button type=""submit"" id=""btn"">Enviar reporte</button>
  </form>
  <div id=""msg""></div>
</div>
<script>
  const f = document.getElementById('f');
  const btn = document.getElementById('btn');
  const msg = document.getElementById('msg');
  f.addEventListener('submit', async (e) => {{
    e.preventDefault();
    btn.disabled = true; btn.textContent = 'Enviando…'; msg.innerHTML = '';
    const fd = new FormData(f);
    fd.append('codigo', {System.Text.Json.JsonSerializer.Serialize(codigo)});
    try {{
      const r = await fetch('/api/public/{slug}/reports', {{ method:'POST', body: fd }});
      if (r.status === 201) {{
        f.style.display='none';
        msg.innerHTML = '<div class=""ok"">✅ ¡Reporte enviado! Gracias. El equipo de mantenimiento fue avisado.</div>';
      }} else {{
        const t = await r.text();
        msg.innerHTML = '<div class=""err"">No se pudo enviar ('+r.status+'). '+t+'</div>';
        btn.disabled=false; btn.textContent='Enviar reporte';
      }}
    }} catch (err) {{
      msg.innerHTML = '<div class=""err"">Error de red: '+err+'</div>';
      btn.disabled=false; btn.textContent='Enviar reporte';
    }}
  }});
</script>";
        return Layout($"Reportar {codigo}", inner);
    }

    private static string LandingHtml(string tenantNombre, string slug, List<string> assets, string baseUrl)
    {
        var baseParam = Uri.EscapeDataString(baseUrl);
        var sb = new StringBuilder();
        sb.Append($@"<div class=""card""><h1>Demo — {tenantNombre}</h1>
<div class=""muted"">Escanea el QR con tu celular para abrir el formulario de reporte.</div>");
        foreach (var codigo in assets)
        {
            sb.Append($@"<div style=""margin-top:20px;text-align:center"">
  <div class=""badge"">{codigo}</div><br>
  <img class=""qr"" src=""/api/public/{slug}/assets/{codigo}/qr.png?base={baseParam}"" alt=""QR {codigo}"">
  <div class=""muted"" style=""margin-top:8px"">
    <a href=""/r/{slug}/{codigo}"">/r/{slug}/{codigo}</a>
  </div>
</div>");
        }
        if (assets.Count == 0)
            sb.Append(@"<p class=""muted"">No hay activos sembrados todavía.</p>");
        sb.Append("</div>");
        return Layout($"Demo {tenantNombre}", sb.ToString());
    }

    private static string NotFoundHtml(string codigo) =>
        Layout("No encontrado",
            $@"<div class=""card""><h1>Activo no encontrado</h1>
<div class=""muted"">No existe un activo con código <code>{codigo}</code> para este cliente.</div></div>");
}
