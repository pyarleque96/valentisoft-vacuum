using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Infrastructure.Auth;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Controllers;

// Login/logout del panel admin (auth por cookie). El usuario se valida contra la tabla
// Users y DEBE pertenecer al tenant del subdominio actual.
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, ITenantContext tenant, ILogger<AuthController> logger)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
    }

    // Home del subdominio: si hay sesión -> panel de sites; si no -> login.
    [HttpGet("/")]
    public IActionResult Home()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/admin/sites");
        return Redirect("/login");
    }

    [HttpGet("/login")]
    public async Task<IActionResult> LoginPage([FromQuery] string? error)
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/admin/sites");
        var tenantName = await CurrentTenantNameAsync();
        return Content(LoginHtml(tenantName, error), "text/html; charset=utf-8");
    }

    [HttpPost("/login")]
    public async Task<IActionResult> Login([FromForm] string? email, [FromForm] string? password, [FromForm] string? remember)
    {
        if (_tenant.TenantId is not Guid tid)
            return Redirect("/login?error=1");

        var mail = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == mail && u.TenantId == tid);

        if (user is null || !PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            _logger.LogWarning("Login fallido para {Email}", mail);
            return Redirect("/login?error=1");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName),
            new(ClaimTypes.Role, user.Role),
            new("tenant", user.TenantId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var persistent = !string.IsNullOrEmpty(remember);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = persistent,
                ExpiresUtc = persistent ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
            });

        return Redirect("/admin/sites");
    }

    [HttpGet("/logout")]
    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }

    private async Task<string> CurrentTenantNameAsync()
    {
        if (_tenant.TenantId is not Guid tid) return "ValentiSoft";
        var t = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == tid);
        return t?.Nombre ?? "ValentiSoft";
    }

    private static string LoginHtml(string tenantNombre, string? error)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var errBox = string.IsNullOrEmpty(error) ? "" :
            @"<div class=""err"">Invalid email or password.</div>";
        return $@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Sign in · {tenantH}</title>
<link rel=""icon"" href=""/favicon.ico"" sizes=""any"">
<link rel=""icon"" type=""image/png"" href=""/favicon-32.png"">
<style>
  :root {{ color-scheme: light; }} * {{ box-sizing:border-box; }}
  body {{ margin:0; min-height:100vh; min-height:100dvh; display:flex; align-items:center; justify-content:center;
    background:#eef1f5; color:#334155; font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; padding:18px; }}
  .card {{ background:#fff; border:1px solid #e5e9f0; border-radius:16px; padding:28px 24px; width:100%; max-width:400px;
    box-shadow:0 12px 40px rgba(15,23,42,.10); }}
  .brand {{ display:flex; flex-direction:column; align-items:center; text-align:center; margin-bottom:20px; }}
  .logo {{ width:64px; height:64px; border-radius:16px; background:#fff url(/images/brand/mastercorp-logo.png) no-repeat 50%/contain;
    border:1px solid #e5e9f0; box-shadow:0 8px 22px rgba(15,23,42,.10); margin-bottom:12px; }}
  h1 {{ font-size:20px; margin:0; color:#1560A8; }}
  .sub {{ color:#64748b; font-size:13px; margin-top:2px; }}
  label {{ display:block; font-size:13px; font-weight:600; color:#475569; margin:14px 0 6px; }}
  input[type=email], input[type=password] {{ width:100%; padding:12px; border-radius:10px; border:1px solid #cbd5e1;
    font-size:16px; color:#1f2937; }}
  input:focus {{ outline:none; border-color:#1560A8; box-shadow:0 0 0 3px rgba(21,96,168,.15); }}
  input::placeholder {{ color:#cbd5e1; }}
  .row {{ display:flex; align-items:center; gap:8px; margin-top:14px; font-size:14px; color:#334155; }}
  .row input {{ width:18px; height:18px; accent-color:#1560A8; }}
  button {{ width:100%; margin-top:20px; padding:13px; border:0; border-radius:10px; background:#1560A8; color:#fff;
    font-weight:700; font-size:15px; cursor:pointer; }}
  button:hover {{ background:#0f4c85; }}
  .err {{ background:#fef2f2; border:1px solid #fecaca; color:#b91c1c; padding:11px 12px; border-radius:10px; font-size:14px; margin-bottom:14px; }}
</style></head>
<body>
  <form class=""card"" method=""post"" action=""/login"">
    <div class=""brand"">
      <div class=""logo"" role=""img"" aria-label=""ValentiSoft""></div>
      <h1>Sign in</h1>
      <div class=""sub"">{tenantH} · Housekeeping</div>
    </div>
    {errBox}
    <label for=""email"">Email</label>
    <input type=""email"" id=""email"" name=""email"" required autocomplete=""username"" placeholder=""you@company.com"">
    <label for=""password"">Password</label>
    <input type=""password"" id=""password"" name=""password"" required autocomplete=""current-password"">
    <label class=""row""><input type=""checkbox"" name=""remember"" value=""1""> Keep me signed in</label>
    <button type=""submit"">Sign in</button>
  </form>
</body></html>";
    }
}
