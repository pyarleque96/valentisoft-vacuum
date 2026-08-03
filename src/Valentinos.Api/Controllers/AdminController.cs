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

    // ---------- Lista de sites ----------
    [HttpGet("/admin/sites")]
    public async Task<IActionResult> Sites()
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        if (tenant is null) return Redirect("/login");

        var sites = await _db.Sites.IgnoreQueryFilters()
            .Where(s => s.TenantId == Tid).OrderBy(s => s.Code).ToListAsync();
        var counts = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.TenantId == Tid && a.Estado == AssetEstado.Activo)
            .GroupBy(a => a.SiteId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var countBySite = counts.ToDictionary(x => x.Key, x => x.N);

        return Content(SitesHtml(tenant.Nombre, sites, countBySite), "text/html; charset=utf-8");
    }

    // ---------- Crear site ----------
    [HttpPost("/admin/sites")]
    public async Task<IActionResult> CreateSite([FromForm] string? code, [FromForm] string? name, [FromForm] string? cc)
    {
        if (Tid == Guid.Empty) return Redirect("/login");
        var c = (code ?? "").Trim();
        var n = string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(c) ? "New site" : $"Site {c}") : name.Trim();
        if (string.IsNullOrWhiteSpace(c)) return Redirect("/admin/sites");

        _db.Sites.Add(new Site
        {
            Code = c,
            Name = n,
            Slug = await UniqueSlugAsync(),
            CcEmails = NormalizeEmails(cc)
        });
        await _db.SaveChangesAsync();
        return Redirect("/admin/sites");
    }

    // ---------- Configurar un site (editar + vacuums) ----------
    [HttpGet("/admin/sites/{id:guid}")]
    public async Task<IActionResult> SiteConfig(Guid id)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        var site = await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id && s.TenantId == Tid);
        if (tenant is null || site is null) return NotFound();

        var vacuums = await _db.Assets.IgnoreQueryFilters()
            .Where(a => a.SiteId == site.Id && a.Estado == AssetEstado.Activo)
            .OrderBy(a => a.Codigo).Select(a => a.Codigo).ToListAsync();

        return Content(SiteConfigHtml(tenant.Nombre, site, vacuums), "text/html; charset=utf-8");
    }

    [HttpPost("/admin/sites/{id:guid}")]
    public async Task<IActionResult> SaveSite(Guid id, [FromForm] string? code, [FromForm] string? name, [FromForm] string? cc)
    {
        var site = await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id && s.TenantId == Tid);
        if (site is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(code)) site.Code = code.Trim();
        if (!string.IsNullOrWhiteSpace(name)) site.Name = name.Trim();
        site.CcEmails = NormalizeEmails(cc);
        await _db.SaveChangesAsync();
        return Redirect($"/admin/sites/{id}");
    }

    // ---------- Agregar vacuums a un site ----------
    [HttpPost("/admin/sites/{id:guid}/vacuums")]
    public async Task<IActionResult> AddVacuums(Guid id, [FromForm] int count)
    {
        var site = await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id && s.TenantId == Tid);
        if (site is null) return NotFound();
        count = Math.Clamp(count, 0, 200);
        if (count == 0) return Redirect($"/admin/sites/{id}");

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
        return Redirect($"/admin/sites/{id}");
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

    private async Task<string> UniqueSlugAsync()
    {
        const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = new Random();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var sb = new StringBuilder("site-");
            for (var i = 0; i < 5; i++) sb.Append(chars[rnd.Next(chars.Length)]);
            var slug = sb.ToString();
            if (!await _db.Sites.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug)) return slug;
        }
        return "site-" + Guid.NewGuid().ToString("N")[..6];
    }

    // ---------- HTML ----------
    private string SitesHtml(string tenantNombre, List<Site> sites, Dictionary<Guid, int> counts)
    {
        var rows = new StringBuilder();
        foreach (var s in sites)
        {
            var n = counts.TryGetValue(s.Id, out var c) ? c : 0;
            var cc = string.IsNullOrWhiteSpace(s.CcEmails) ? "<span class=\"muted\">—</span>" : WebUtility.HtmlEncode(s.CcEmails);
            rows.Append($@"<tr>
              <td><b>{WebUtility.HtmlEncode(s.Name)}</b><div class=""mono muted"">/{WebUtility.HtmlEncode(s.Slug)}</div></td>
              <td class=""mono"">{WebUtility.HtmlEncode(s.Code)}</td>
              <td>{n}</td>
              <td class=""cccell"">{cc}</td>
              <td><a class=""lnk"" href=""/admin/sites/{s.Id}"">Configure</a></td>
            </tr>");
        }
        if (sites.Count == 0)
            rows.Append(@"<tr><td colspan=""5"" class=""muted"" style=""text-align:center;padding:20px"">No sites yet.</td></tr>");

        var body = $@"
  <div class=""card"">
    <h2>Sites</h2>
    <div class=""tblwrap""><table>
      <thead><tr><th>Site</th><th>Code</th><th>Vacuums</th><th>Recipients (CC)</th><th></th></tr></thead>
      <tbody>{rows}</tbody>
    </table></div>
  </div>

  <div class=""card"">
    <h2>New site</h2>
    <form method=""post"" action=""/admin/sites"" class=""grid"">
      <div><label>Code</label><input name=""code"" placeholder=""e.g. 070"" required></div>
      <div><label>Name</label><input name=""name"" placeholder=""e.g. Site 070""></div>
      <div class=""full""><label>Recipients (CC) — comma separated</label><input name=""cc"" placeholder=""manager@company.com""></div>
      <div class=""full""><button type=""submit"">Create site</button></div>
    </form>
  </div>";
        return AdminLayout($"Sites · {tenantNombre}", tenantNombre, body);
    }

    private string SiteConfigHtml(string tenantNombre, Site site, List<string> vacuums)
    {
        var chips = new StringBuilder();
        foreach (var v in vacuums) chips.Append($@"<span class=""chip mono"">{WebUtility.HtmlEncode(v)}</span>");
        if (vacuums.Count == 0) chips.Append(@"<span class=""muted"">No vacuums yet.</span>");

        var body = $@"
  <div class=""crumb""><a href=""/admin/sites"">← Sites</a></div>

  <div class=""card"">
    <h2>{WebUtility.HtmlEncode(site.Name)} <span class=""mono muted"">/{WebUtility.HtmlEncode(site.Slug)}</span></h2>
    <form method=""post"" action=""/admin/sites/{site.Id}"" class=""grid"">
      <div><label>Code</label><input name=""code"" value=""{WebUtility.HtmlEncode(site.Code)}"" required></div>
      <div><label>Name</label><input name=""name"" value=""{WebUtility.HtmlEncode(site.Name)}"" required></div>
      <div class=""full""><label>Recipients (CC) — comma separated</label>
        <input name=""cc"" value=""{WebUtility.HtmlEncode(site.CcEmails ?? "")}"" placeholder=""manager@company.com""></div>
      <div class=""full""><button type=""submit"">Save changes</button></div>
    </form>
  </div>

  <div class=""card"">
    <h2>Vacuums ({vacuums.Count})</h2>
    <div class=""chips"">{chips}</div>
    <form method=""post"" action=""/admin/sites/{site.Id}/vacuums"" class=""addrow"">
      <label>Add</label>
      <input type=""number"" name=""count"" value=""1"" min=""1"" max=""200"" style=""width:90px"">
      <span class=""muted"">vacuums (numbered continuing from the last)</span>
      <button type=""submit"">Add</button>
    </form>
  </div>";
        return AdminLayout($"{site.Name} · {tenantNombre}", tenantNombre, body);
    }

    private string AdminLayout(string title, string tenantNombre, string body)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var userH = WebUtility.HtmlEncode(UserName);
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
  .who {{ color:#64748b; font-size:13px; }} .who b {{ color:#334155; }}
  .out {{ margin-left:12px; text-decoration:none; color:#1560A8; font-weight:600; font-size:13px; }}
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
  label {{ display:block; font-size:13px; font-weight:600; color:#475569; margin:0 0 6px; }}
  input {{ width:100%; padding:11px; border-radius:9px; border:1px solid #cbd5e1; font-size:15px; color:#1f2937; }}
  input:focus {{ outline:none; border-color:#1560A8; box-shadow:0 0 0 3px rgba(21,96,168,.15); }}
  input::placeholder {{ color:#cbd5e1; }}
  .grid {{ display:grid; grid-template-columns:1fr 1fr; gap:14px; }} .grid .full {{ grid-column:1/-1; }}
  button {{ background:#1560A8; color:#fff; border:0; border-radius:9px; padding:11px 18px; font-weight:700; font-size:14px; cursor:pointer; }}
  button:hover {{ background:#0f4c85; }}
  .crumb {{ margin-bottom:12px; }} .crumb a {{ color:#1560A8; text-decoration:none; font-weight:600; font-size:14px; }}
  .chips {{ display:flex; flex-wrap:wrap; gap:8px; margin-bottom:16px; }}
  .chip {{ background:#eaf1f9; color:#1560A8; border-radius:8px; padding:6px 10px; font-weight:600; font-size:13px; }}
  .addrow {{ display:flex; align-items:center; gap:10px; flex-wrap:wrap; }} .addrow label {{ margin:0; }}
  @media (max-width:560px) {{ .grid {{ grid-template-columns:1fr; }} }}
</style></head>
<body>
  <div class=""top""><div class=""topin"">
    <div class=""logo""></div>
    <div class=""brand"">ValentiSoft<div class=""sub"">{tenantH} · Admin</div></div>
    <div class=""spacer""></div>
    <div class=""who"">Signed in as <b>{userH}</b></div>
    <a class=""out"" href=""/logout"">Sign out</a>
  </div></div>
  <div class=""wrap"">{body}</div>
</body></html>";
    }
}
