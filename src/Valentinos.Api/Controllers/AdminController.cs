using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Entities;
using Valentinos.Domain.Enums;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Controllers;

// Panel de administración (solo admin autenticado): gestión de sites de un tenant,
// sus encargados (CC del reporte) y sus vacuums.
[ApiController]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase, IActionFilter
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public AdminController(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private Guid Tid => _tenant.TenantId ?? Guid.Empty;
    private string UserName => User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

    // Defensa en profundidad: el tenant de la cookie DEBE coincidir con el del subdominio.
    // (Las cookies son host-only, pero esto evita cualquier cruce de tenant.)
    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var claim = User.FindFirstValue("tenant");
        if (!Guid.TryParse(claim, out var userTenant) || userTenant == Guid.Empty || userTenant != Tid)
            context.Result = new RedirectResult("/login");
    }
    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context) { }

    // Resuelve un site por su clave (guid o slug), validando el tenant.
    private Task<Site?> FindSiteAsync(string key) =>
        Guid.TryParse(key, out var gid)
            ? _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == gid && s.TenantId == Tid)
            : _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Slug == key && s.TenantId == Tid);

    // ---------- Lista de sites ----------
    [HttpGet("/admin/sites")]
    public async Task<IActionResult> Sites()
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        if (tenant is null) return Redirect("/login");

        var sites = await _db.Sites.IgnoreQueryFilters()
            .Where(s => s.TenantId == Tid).OrderByDescending(s => s.Code).ToListAsync();
        var counts = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.TenantId == Tid && a.Estado == AssetEstado.Activo)
            .GroupBy(a => a.SiteId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var countBySite = counts.ToDictionary(x => x.Key, x => x.N);

        return Content(SitesHtml(tenant.Nombre, sites, countBySite), "text/html; charset=utf-8");
    }

    // ---------- Crear site ----------
    [HttpPost("/admin/sites")]
    public async Task<IActionResult> CreateSite([FromForm] string? code, [FromForm] string? cc)
    {
        if (Tid == Guid.Empty) return Redirect("/login");
        var c = (code ?? "").Trim();
        if (string.IsNullOrWhiteSpace(c)) return Redirect("/admin/sites");

        _db.Sites.Add(new Site
        {
            Code = c,
            Slug = await UniqueSlugAsync(),
            CcEmails = NormalizeEmails(cc)
        });
        await _db.SaveChangesAsync();
        return Redirect("/admin/sites");
    }

    // ---------- Configurar un site (editar + vacuums). Acepta guid o slug. ----------
    [HttpGet("/admin/sites/{key}")]
    public async Task<IActionResult> SiteConfig(string key)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        var site = await FindSiteAsync(key);
        if (tenant is null || site is null) return NotFound();

        var vacuums = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.SiteId == site.Id && a.Estado == AssetEstado.Activo)
            .OrderBy(a => a.Codigo).Select(a => a.Codigo).ToListAsync();

        return Content(SiteConfigHtml(tenant.Nombre, site, vacuums), "text/html; charset=utf-8");
    }

    [HttpPost("/admin/sites/{key}")]
    public async Task<IActionResult> SaveSite(string key, [FromForm] string? code, [FromForm] string? cc)
    {
        var site = await FindSiteAsync(key);
        if (site is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(code)) site.Code = code.Trim();
        site.CcEmails = NormalizeEmails(cc);
        await _db.SaveChangesAsync();
        return Redirect($"/admin/sites/{site.Slug}");
    }

    // ---------- Agregar vacuums a un site ----------
    [HttpPost("/admin/sites/{key}/vacuums")]
    public async Task<IActionResult> AddVacuums(string key, [FromForm] int count)
    {
        var site = await FindSiteAsync(key);
        if (site is null) return NotFound();
        count = Math.Clamp(count, 0, 200);
        if (count == 0) return Redirect($"/admin/sites/{site.Slug}");

        var tipo = await _db.AssetTypes.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == Tid && t.Prefijo == "VAC");
        if (tipo is null)
        {
            tipo = AssetType.Create("Vacuum", "VAC");
            _db.AssetTypes.Add(tipo);
            await _db.SaveChangesAsync();
        }

        // Numeración por-site: continúa desde el mayor VAC-### existente en el site.
        var existing = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.SiteId == site.Id).Select(a => a.Codigo).ToListAsync();
        var maxN = existing.Select(ParseVacNumber).DefaultIfEmpty(0).Max();

        for (var i = 1; i <= count; i++)
        {
            _db.Assets.Add(new Asset
            {
                AssetTypeId = tipo.Id,
                SiteId = site.Id,
                Codigo = $"VAC-{maxN + i:D3}",
                Estado = AssetEstado.Activo
            });
        }
        await _db.SaveChangesAsync();
        return Redirect($"/admin/sites/{site.Slug}");
    }

    // ---------- helpers ----------
    private static int ParseVacNumber(string codigo)
    {
        var dash = codigo.LastIndexOf('-');
        return dash >= 0 && int.TryParse(codigo[(dash + 1)..], out var n) ? n : 0;
    }

    private static string? NormalizeEmails(string? cc)
    {
        if (string.IsNullOrWhiteSpace(cc)) return null;
        var parts = cc.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    // Rutas top-level reservadas: un slug no puede colisionar con ellas.
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
        { "admin", "login", "logout", "api", "qr", "e", "f", "images", "favicon" };

    private async Task<string> UniqueSlugAsync()
    {
        const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = new Random();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 6; i++) sb.Append(chars[rnd.Next(chars.Length)]);
            var slug = sb.ToString();
            if (ReservedSlugs.Contains(slug)) continue;
            if (!await _db.Sites.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug)) return slug;
        }
        return Guid.NewGuid().ToString("N")[..8];
    }

    // ---------- HTML ----------
    private string SitesHtml(string tenantNombre, List<Site> sites, Dictionary<Guid, int> counts)
    {
        const string gear = @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><circle cx=""12"" cy=""12"" r=""3""></circle><path d=""M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z""></path></svg>";
        const string dash = @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><line x1=""18"" y1=""20"" x2=""18"" y2=""10""></line><line x1=""12"" y1=""20"" x2=""12"" y2=""4""></line><line x1=""6"" y1=""20"" x2=""6"" y2=""14""></line></svg>";
        const string qr = @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><rect x=""3"" y=""3"" width=""7"" height=""7""></rect><rect x=""14"" y=""3"" width=""7"" height=""7""></rect><rect x=""3"" y=""14"" width=""7"" height=""7""></rect><line x1=""14"" y1=""14"" x2=""14"" y2=""21""></line><line x1=""21"" y1=""14"" x2=""21"" y2=""21""></line><line x1=""17"" y1=""17"" x2=""17"" y2=""17""></line></svg>";
        var rows = new StringBuilder();
        foreach (var s in sites)
        {
            var n = counts.TryGetValue(s.Id, out var c) ? c : 0;
            var cc = string.IsNullOrWhiteSpace(s.CcEmails) ? "<span class=\"muted\">—</span>" : WebUtility.HtmlEncode(s.CcEmails);
            rows.Append($@"<tr>
              <td><b class=""mono"">{WebUtility.HtmlEncode(s.Code)}</b></td>
              <td>{n}</td>
              <td class=""cccell"">{cc}</td>
              <td class=""acts"">
                <a class=""ico"" href=""/admin/sites/{s.Slug}"" title=""Configure"" aria-label=""Configure"">{gear}</a>
                <a class=""ico"" href=""/reports?site={s.Slug}"" title=""KPIs"" aria-label=""KPIs"">{dash}</a>
                <a class=""ico"" href=""/admin/sites/{s.Slug}/qr"" title=""QR codes"" aria-label=""QR codes"">{qr}</a>
              </td>
            </tr>");
        }
        if (sites.Count == 0)
            rows.Append(@"<tr><td colspan=""4"" class=""muted"" style=""text-align:center;padding:20px"">No sites yet.</td></tr>");

        var body = $@"
  <div class=""card"">
    <h2>Sites</h2>
    <div class=""tblwrap""><table>
      <thead><tr><th>Site</th><th>Vacuums</th><th>Recipients (CC)</th><th></th></tr></thead>
      <tbody>{rows}</tbody>
    </table></div>
  </div>

  <!-- 'New site' oculto por ahora: inputs y submit deshabilitados. -->
  <div class=""card"" style=""display:none"">
    <h2>New site</h2>
    <form method=""post"" action=""/admin/sites"" class=""grid"">
      <div><label>Code</label><input name=""code"" placeholder=""e.g. 070"" disabled></div>
      <div class=""full""><label>Recipients (CC) — comma separated</label><input name=""cc"" placeholder=""manager@company.com"" disabled></div>
      <div class=""full""><button type=""submit"" disabled>Create site</button></div>
    </form>
  </div>";
        return AdminLayout($"Sites · {tenantNombre}", tenantNombre, body);
    }

    private string SiteConfigHtml(string tenantNombre, Site site, List<string> vacuums)
    {
        var chips = new StringBuilder();
        foreach (var v in vacuums) chips.Append($@"<span class=""chip mono"">{WebUtility.HtmlEncode(v)}</span>");
        if (vacuums.Count == 0) chips.Append(@"<span class=""muted"">No vacuums yet.</span>");

        const string qrIco = @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><rect x=""3"" y=""3"" width=""7"" height=""7""></rect><rect x=""14"" y=""3"" width=""7"" height=""7""></rect><rect x=""3"" y=""14"" width=""7"" height=""7""></rect><line x1=""14"" y1=""14"" x2=""14"" y2=""21""></line><line x1=""21"" y1=""14"" x2=""21"" y2=""21""></line><line x1=""17"" y1=""17"" x2=""17"" y2=""17""></line></svg>";
        const string dashIco = @"<svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><line x1=""18"" y1=""20"" x2=""18"" y2=""10""></line><line x1=""12"" y1=""20"" x2=""12"" y2=""4""></line><line x1=""6"" y1=""20"" x2=""6"" y2=""14""></line></svg>";
        var body = $@"
  <div class=""crumb""><a href=""/admin/sites"">← Sites</a></div>

  <div class=""siteacts"">
    <a href=""/admin/sites/{site.Slug}/qr"">{qrIco} QR codes</a>
    <a href=""/reports?site={site.Slug}"">{dashIco} Dashboard (KPIs)</a>
  </div>

  <div class=""card"">
    <h2>Site {WebUtility.HtmlEncode(site.Code)} <span class=""mono muted"">/{WebUtility.HtmlEncode(site.Slug)}</span></h2>
    <form id=""siteForm"" method=""post"" action=""/admin/sites/{site.Slug}"" class=""grid"">
      <div><label>Code</label><input name=""code"" value=""{WebUtility.HtmlEncode(site.Code)}"" required></div>
      <div class=""full""><label>Recipients (CC) — comma separated</label>
        <input id=""ccInput"" name=""cc"" value=""{WebUtility.HtmlEncode(site.CcEmails ?? "")}"" placeholder=""manager@company.com"">
        <div class=""fielderr"" id=""ccErr""></div></div>
      <div class=""full""><button type=""submit"">Save changes</button></div>
    </form>
  </div>

  <div class=""card"">
    <h2>Vacuums ({vacuums.Count})</h2>
    <div class=""chips"">{chips}</div>
    <form id=""addForm"" method=""post"" action=""/admin/sites/{site.Slug}/vacuums"" class=""addrow"">
      <label>Add</label>
      <input type=""number"" id=""addCount"" name=""count"" value=""1"" min=""1"" max=""200"" style=""width:90px"">
      <span class=""muted"">vacuums (numbered continuing from the last)</span>
      <button type=""submit"">Add</button>
    </form>
  </div>

  <div class=""modal-bg"" id=""addBg"">
    <div class=""modal"" role=""dialog"" aria-modal=""true"">
      <div class=""mico""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><line x1=""12"" y1=""5"" x2=""12"" y2=""19""></line><line x1=""5"" y1=""12"" x2=""19"" y2=""12""></line></svg></div>
      <h3>Add vacuums?</h3>
      <p>You're about to add <b id=""addN"">1</b> vacuum(s) to <b>Site {site.Code}</b>. They will be numbered continuing from the last one. Continue?</p>
      <div class=""modal-actions"">
        <button type=""button"" class=""mbtn ghost"" id=""addCancel"">Cancel</button>
        <button type=""button"" class=""mbtn primary"" id=""addConfirm"">Add</button>
      </div>
    </div>
  </div>
  <script>
    (function () {{
      var form = document.getElementById('addForm'), bg = document.getElementById('addBg'),
          nEl = document.getElementById('addN'), cnt = document.getElementById('addCount');
      form.addEventListener('submit', function (e) {{ e.preventDefault(); nEl.textContent = cnt.value || '1'; bg.classList.add('open'); }});
      document.getElementById('addCancel').addEventListener('click', function () {{ bg.classList.remove('open'); }});
      bg.addEventListener('click', function (e) {{ if (e.target === bg) bg.classList.remove('open'); }});
      document.addEventListener('keydown', function (e) {{ if (e.key === 'Escape') bg.classList.remove('open'); }});
      document.getElementById('addConfirm').addEventListener('click', function () {{ form.submit(); }});
    }})();
    // Validación de Recipients (CC): emails separados por coma; si hay error, muestra ejemplo.
    (function () {{
      var f = document.getElementById('siteForm'), inp = document.getElementById('ccInput'), err = document.getElementById('ccErr');
      var re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
      function badEmails() {{
        var raw = (inp.value || '').trim();
        if (!raw) return [];
        return raw.split(/[,;\n]/).map(function (s) {{ return s.trim(); }}).filter(Boolean).filter(function (p) {{ return !re.test(p); }});
      }}
      function clearErr() {{ err.classList.remove('show'); inp.classList.remove('invalid'); }}
      inp.addEventListener('input', clearErr);
      f.addEventListener('submit', function (e) {{
        var bad = badEmails();
        if (bad.length) {{
          e.preventDefault();
          err.innerHTML = 'Please enter valid emails separated by commas. Example: <code>manager@company.com, other@company.com</code>. Check: <b>' + bad.map(function (b) {{ return b.replace(/</g,'&lt;'); }}).join(', ') + '</b>';
          err.classList.add('show'); inp.classList.add('invalid'); inp.focus();
        }}
      }});
    }})();
  </script>";
        return AdminLayout($"Site {site.Code} · {tenantNombre}", tenantNombre, body);
    }

    // Color de avatar estable por nombre (parece aleatorio pero es consistente por usuario).
    private static string AvatarColor(string name)
    {
        var palette = new[] { "#1560A8", "#0f766e", "#7c3aed", "#be185d", "#c2410c", "#15803d", "#0369a1", "#b45309", "#4338ca", "#0d9488" };
        var h = 0; foreach (var ch in name ?? "") h = (h * 31 + ch) & 0x7fffffff;
        return palette[h % palette.Length];
    }
    private static string Initial(string name) =>
        string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();

    private string AdminLayout(string title, string tenantNombre, string body)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var userH = WebUtility.HtmlEncode(UserName);
        var avatarColor = AvatarColor(UserName);
        var initial = WebUtility.HtmlEncode(Initial(UserName));
        return $@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>{WebUtility.HtmlEncode(title)}</title>
<link rel=""icon"" href=""/favicon.ico"" sizes=""any"">
<link rel=""icon"" type=""image/png"" href=""/favicon-32.png"">
<style>
  :root {{ color-scheme: light; }} * {{ box-sizing:border-box; }}
  body {{ margin:0; background:#eef1f5; color:#334155; font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; }}
  .top {{ background:#fff; border-bottom:1px solid #e5e9f0; }}
  .topin {{ max-width:820px; margin:0 auto; padding:14px 18px; display:flex; align-items:center; gap:12px; }}
  .logo {{ width:36px; height:36px; border-radius:9px; flex:0 0 auto; background:#fff url(/images/brand/mastercorp-logo.png) no-repeat 50%/contain; border:1px solid #e5e9f0; }}
  .brand {{ font-weight:800; color:#1560A8; font-size:16px; }} .brand .sub {{ color:#64748b; font-size:12px; font-weight:500; }}
  .spacer {{ flex:1; }}
  .user {{ position:relative; }}
  .userbtn {{ display:flex; align-items:center; gap:10px; background:transparent; border:0; cursor:pointer; padding:4px; border-radius:999px; }}
  .userbtn:hover {{ background:#f1f5f9; }}
  .avatar {{ width:34px; height:34px; border-radius:50%; flex:0 0 auto; color:#fff; font-weight:700; font-size:14px;
    display:flex; align-items:center; justify-content:center; }}
  .uname {{ color:#334155; font-weight:600; font-size:14px; }}
  .caret {{ color:#94a3b8; font-size:11px; }}
  .dropdown {{ position:absolute; right:0; top:calc(100% + 6px); background:#fff; border:1px solid #e5e9f0; border-radius:12px;
    box-shadow:0 12px 30px rgba(15,23,42,.15); min-width:170px; padding:6px; display:none; z-index:50; }}
  .dropdown.open {{ display:block; }}
  .dropdown a {{ display:flex; align-items:center; gap:10px; padding:10px 12px; border-radius:8px; text-decoration:none; color:#334155; font-size:14px; font-weight:600; }}
  .dropdown a:hover {{ background:#f1f5f9; color:#b91c1c; }}
  .dropdown svg {{ width:17px; height:17px; }}
  @media (max-width:560px) {{ .uname {{ display:none; }} }}
  .wrap {{ max-width:820px; margin:0 auto; padding:18px; }}
  .card {{ background:#fff; border:1px solid #e5e9f0; border-radius:14px; padding:18px; margin-bottom:14px; box-shadow:0 6px 20px rgba(15,23,42,.05); }}
  h2 {{ font-size:16px; margin:0 0 14px; color:#1560A8; }}
  table {{ width:100%; border-collapse:collapse; font-size:14px; }}
  .tblwrap {{ overflow-x:auto; }}
  th,td {{ text-align:left; padding:10px 8px; border-bottom:1px solid #eef2f6; vertical-align:top; }}
  th {{ color:#64748b; font-size:11px; text-transform:uppercase; letter-spacing:.3px; }}
  .mono {{ font-family:ui-monospace,monospace; }} .muted {{ color:#94a3b8; }}
  .cccell {{ max-width:220px; word-break:break-word; font-size:13px; }}
  .lnk {{ color:#1560A8; text-decoration:none; font-weight:600; }}
  .acts {{ white-space:nowrap; text-align:right; }}
  .ico {{ display:inline-flex; align-items:center; justify-content:center; width:36px; height:36px; border:1px solid #e5e9f0; border-radius:9px; color:#1560A8; margin-left:6px; text-decoration:none; transition:.15s; }}
  .ico:hover {{ background:#eef4fb; border-color:#1560A8; }} .ico svg {{ width:18px; height:18px; }}
  .siteacts {{ display:flex; gap:10px; flex-wrap:wrap; margin-bottom:14px; }}
  .siteacts a {{ display:inline-flex; align-items:center; gap:8px; text-decoration:none; background:#eef4fb; color:#1560A8; font-weight:700; font-size:14px; border-radius:9px; padding:10px 14px; }}
  .siteacts a:hover {{ background:#dbe8f7; }} .siteacts svg {{ width:18px; height:18px; }}
  label {{ display:block; font-size:13px; font-weight:600; color:#475569; margin:0 0 6px; }}
  input {{ width:100%; padding:11px; border-radius:9px; border:1px solid #cbd5e1; font-size:15px; color:#1f2937; }}
  input:focus {{ outline:none; border-color:#1560A8; box-shadow:0 0 0 3px rgba(21,96,168,.15); }}
  input::placeholder {{ color:#cbd5e1; }}
  input.invalid {{ border-color:#fca5a5; box-shadow:0 0 0 3px rgba(239,68,68,.12); }}
  .fielderr {{ color:#b91c1c; font-size:12.5px; margin-top:6px; display:none; line-height:1.5; }}
  .fielderr.show {{ display:block; }} .fielderr code {{ background:#fef2f2; padding:1px 5px; border-radius:5px; font-size:12px; }}
  .grid {{ display:grid; grid-template-columns:1fr 1fr; gap:14px; }} .grid .full {{ grid-column:1/-1; }}
  button {{ background:#1560A8; color:#fff; border:0; border-radius:9px; padding:11px 18px; font-weight:700; font-size:14px; cursor:pointer; }}
  button:hover {{ background:#0f4c85; }}
  .crumb {{ margin-bottom:12px; }} .crumb a {{ color:#1560A8; text-decoration:none; font-weight:600; font-size:14px; }}
  .chips {{ display:flex; flex-wrap:wrap; gap:8px; margin-bottom:16px; }}
  .chip {{ background:#eaf1f9; color:#1560A8; border-radius:8px; padding:6px 10px; font-weight:600; font-size:13px; }}
  .addrow {{ display:flex; align-items:center; gap:10px; flex-wrap:wrap; }} .addrow label {{ margin:0; }}
  .modal-bg {{ position:fixed; inset:0; background:rgba(15,23,42,.55); display:none; align-items:center; justify-content:center; z-index:100; padding:18px; }}
  .modal-bg.open {{ display:flex; }}
  .modal {{ background:#fff; border-radius:16px; padding:22px; max-width:400px; width:100%; box-shadow:0 24px 60px rgba(15,23,42,.35); }}
  .modal .mico {{ width:46px; height:46px; border-radius:12px; background:#eaf1f9; color:#1560A8; display:flex; align-items:center; justify-content:center; margin-bottom:12px; }}
  .modal .mico svg {{ width:24px; height:24px; }}
  .modal h3 {{ margin:0 0 6px; font-size:18px; color:#0f172a; }}
  .modal p {{ margin:0 0 18px; color:#475569; font-size:14px; line-height:1.5; }} .modal p b {{ color:#0f172a; }}
  .modal-actions {{ display:flex; gap:10px; justify-content:flex-end; }}
  .mbtn {{ padding:11px 18px; border-radius:10px; font-weight:700; font-size:14px; cursor:pointer; border:0; }}
  .mbtn.ghost {{ background:#f1f5f9; color:#334155; }} .mbtn.ghost:hover {{ background:#e2e8f0; }}
  .mbtn.primary {{ background:#1560A8; color:#fff; }} .mbtn.primary:hover {{ background:#0f4c85; }}
  @media (max-width:560px) {{
    .grid {{ grid-template-columns:1fr; }}
    /* En móvil ocultar la columna Recipients (CC) del grid. */
    .tblwrap th:nth-child(3), .tblwrap td:nth-child(3) {{ display:none; }}
  }}
</style></head>
<body>
  <div class=""top""><div class=""topin"">
    <div class=""logo""></div>
    <div class=""brand"">{tenantH}<div class=""sub"">Admin</div></div>
    <div class=""spacer""></div>
    <div class=""user"" id=""userMenu"">
      <button class=""userbtn"" id=""userBtn"" aria-haspopup=""true"" aria-expanded=""false"">
        <span class=""avatar"" style=""background:{avatarColor}"">{initial}</span>
        <span class=""uname"">{userH}</span>
        <span class=""caret"">▾</span>
      </button>
      <div class=""dropdown"" id=""userDrop"">
        <a href=""/logout""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><path d=""M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4""></path><polyline points=""16 17 21 12 16 7""></polyline><line x1=""21"" y1=""12"" x2=""9"" y2=""12""></line></svg> Sign out</a>
      </div>
    </div>
  </div></div>
  <div class=""wrap"">{body}</div>
  <script>
    (function () {{
      var btn = document.getElementById('userBtn'), drop = document.getElementById('userDrop'), menu = document.getElementById('userMenu');
      btn.addEventListener('click', function (e) {{ e.stopPropagation(); var open = drop.classList.toggle('open'); btn.setAttribute('aria-expanded', open); }});
      document.addEventListener('click', function (e) {{ if (!menu.contains(e.target)) drop.classList.remove('open'); }});
      document.addEventListener('keydown', function (e) {{ if (e.key === 'Escape') drop.classList.remove('open'); }});
    }})();
  </script>
</body></html>";
    }
}
