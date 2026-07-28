using System.Net;
using System.Text;
using System.Text.Json;
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
  :root {{ color-scheme: light dark; }}
  body {{ font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 0;
         min-height: 100vh; min-height: 100dvh; display: flex;
         background: #0f172a; color: #e2e8f0; }}
  /* margin:auto centra vertical y horizontalmente; si el contenido es más alto
     que la pantalla, los márgenes colapsan y hace scroll sin recortar. */
  .wrap {{ max-width: 560px; width: 100%; margin: auto; padding: 24px 18px;
          box-sizing: border-box; }}
  /* Variante 'fill': ocupa todo el alto y ancla el botón de enviar al fondo. */
  .wrap.fill {{ margin: 0 auto; min-height: 100dvh; display: flex; flex-direction: column; }}
  .wrap.fill > .card {{ flex: 1; display: flex; flex-direction: column; }}
  .wrap.fill form {{ display: flex; flex-direction: column; flex: 1; }}
  .wrap.fill form button[type=""submit""] {{ margin-top: auto; }}
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
  .top {{ display:flex; align-items:flex-start; justify-content:space-between; gap:12px; }}
  .langs {{ display:flex; gap:6px; flex:0 0 auto; }}
  .flag {{ padding:2px; background:transparent; border:0; width:auto; margin:0; cursor:pointer;
           border-radius:999px; line-height:0; opacity:.45; transition:opacity .15s, box-shadow .15s; }}
  .flag:hover {{ opacity:.85; }}
  .flag.active {{ opacity:1; box-shadow:0 0 0 2px #22c55e; }}
  .flag img {{ display:block; border-radius:999px; }}
  .hero {{ text-align:center; padding:20px 0 8px; }}
  /* Placeholder del logo de Valentino's: una V elegante. Reemplazar por el logo real. */
  .logo {{ width:120px; height:120px; margin:8px auto 18px; border-radius:50%;
           display:flex; align-items:center; justify-content:center;
           background:radial-gradient(circle at 32% 28%, #123047, #0b1f30 70%);
           border:1px solid rgba(148,163,184,.25);
           box-shadow:0 12px 34px rgba(2,8,20,.55), inset 0 1px 0 rgba(255,255,255,.06); }}
  .logo span {{ font-family: Georgia, 'Times New Roman', 'Playfair Display', serif;
           font-style: italic; font-weight: 700; font-size: 76px; line-height:1;
           background:linear-gradient(180deg,#e9d9a7,#c9a24b);
           -webkit-background-clip:text; background-clip:text; color:transparent;
           text-shadow:0 1px 1px rgba(0,0,0,.25); letter-spacing:1px;
           padding-right:6px; /* balance visual de la itálica */ }}
  a.btn-report {{ display:block; text-decoration:none; text-align:center; margin-top:22px;
    padding:16px; border-radius:12px; background:#22c55e; color:#052e16;
    font-weight:800; font-size:17px; }}
</style>
</head>
<body><div class=""wrap {wrapClass}"">{bodyInner}</div></body>
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
    <h1 data-i18n=""title"">Report a breakdown</h1>
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
    <label data-i18n=""name"">Your name *</label>
    <input name=""reportadoPor"" required data-i18n-ph=""namePh"" placeholder=""e.g. Ana"">

    <label data-i18n=""desc"">What's wrong with the equipment? *</label>
    <textarea name=""descripcion"" required data-i18n-ph=""descPh"" placeholder=""e.g. won't turn on / makes noise / no suction""></textarea>

    <label data-i18n=""sev"">Severity *</label>
    <select name=""severidad"" required>
      <option value=""Leve"" data-i18n=""sevLeve"">Minor</option>
      <option value=""AMedias"" data-i18n=""sevAMedias"">Partially works</option>
      <option value=""NoFunciona"" selected data-i18n=""sevNoFunciona"">Not working</option>
    </select>

    <label data-i18n=""photos"">Photos (optional)</label>
    <input type=""file"" name=""fotos"" accept=""image/*"" multiple>

    <button type=""submit"" id=""btn"" data-i18n=""send"">Send report</button>
  </form>
  <div id=""msg""></div>
</div>
<script>
  const I18N = {{
    en: {{ title:'Report a breakdown', sub:'Housekeeping', name:'Your name *', namePh:'e.g. Ana',
      desc:""What's wrong with the equipment? *"", descPh:""e.g. won't turn on / makes noise / no suction"",
      sev:'Severity *', sevLeve:'Minor', sevAMedias:'Partially works', sevNoFunciona:'Not working',
      photos:'Photos (optional)', send:'Send report', sending:'Sending…',
      ok:'✅ Report sent! Thank you. The maintenance team has been notified.',
      fail:""Couldn't send"", net:'Network error: ' }},
    es: {{ title:'Reportar avería', sub:'Housekeeping', name:'Tu nombre *', namePh:'Ej. Ana',
      desc:'¿Qué le pasa al equipo? *', descPh:'Ej. no enciende / hace ruido / no aspira',
      sev:'Severidad *', sevLeve:'Leve', sevAMedias:'Funciona a medias', sevNoFunciona:'No funciona',
      photos:'Fotos (opcional)', send:'Enviar reporte', sending:'Enviando…',
      ok:'✅ ¡Reporte enviado! Gracias. El equipo de mantenimiento fue avisado.',
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

  const f = document.getElementById('f');
  const btn = document.getElementById('btn');
  const msg = document.getElementById('msg');
  f.addEventListener('submit', async (e) => {{
    e.preventDefault();
    const d = I18N[LANG];
    btn.disabled = true; btn.textContent = d.sending; msg.innerHTML = '';
    const fd = new FormData(f);
    fd.append('codigo', {codigoJs});
    try {{
      const r = await fetch('/api/public/' + encodeURIComponent({slugJs}) + '/reports', {{ method:'POST', body: fd }});
      if (r.status === 201) {{
        f.style.display='none';
        msg.innerHTML = '<div class=""ok"">'+d.ok+'</div>';
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

  // Inglés por default; respeta la última elección del usuario si existe.
  let init = 'en';
  try {{ const saved = localStorage.getItem('lang'); if (saved) init = saved; }} catch (e) {{}}
  setLang(init);
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
    <!-- Logo placeholder de Valentino's (una V elegante). Reemplazar por el logo real. -->
    <div class=""logo""><span>V</span></div>
    <h1 style=""margin:0"">{tenantH}</h1>
    <div class=""muted"" data-i18n=""prompt"">Found a problem with this equipment?</div>
    <div class=""badge"">{tipoH} · {codigoH}</div>
  </div>

  <a class=""btn-report"" href=""/r/{slugUrl}/{codigoUrl}/reportar"" data-i18n=""report"">Report a breakdown</a>
</div>
<script>
  const I18N = {{
    en: {{ prompt:'Found a problem with this equipment?', report:'Report a breakdown' }},
    es: {{ prompt:'¿Este equipo tiene un problema?', report:'Reportar avería' }}
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
