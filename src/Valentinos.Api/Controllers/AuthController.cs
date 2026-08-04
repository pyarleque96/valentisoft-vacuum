using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.RateLimiting;
using Valentinos.Api.Auth;
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
    private readonly IServiceScopeFactory _scopes;

    public AuthController(AppDbContext db, ITenantContext tenant, ILogger<AuthController> logger,
        IServiceScopeFactory scopes)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
        _scopes = scopes;
    }

    // Home del subdominio: si hay sesión -> panel de sites; si no -> login.
    [HttpGet("/")]
    public IActionResult Home()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/admin/sites");
        return Redirect("/login");
    }

    [HttpGet("/login")]
    public async Task<IActionResult> LoginPage([FromQuery] string? error, [FromQuery] string? reset)
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/admin/sites");
        var tenantName = await CurrentTenantNameAsync();
        return Content(LoginHtml(tenantName, error, reset is not null), "text/html; charset=utf-8");
    }

    [EnableRateLimiting("auth")]
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
            new("stamp", user.SecurityStamp),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        // Sesión persistente y larga (1 año): no expira en uso normal. La invalidación
        // sigue disponible vía SecurityStamp (reset de contraseña) y /logout.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(365)
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

    // ---------- Recuperación de contraseña (código de 6 dígitos por correo) ----------
    private const int CodeMinutes = 15;
    private const int MaxAttempts = 5;

    [HttpGet("/forgot")]
    public IActionResult ForgotPage([FromQuery] string? sent) =>
        Content(ForgotHtml(sent is not null), "text/html; charset=utf-8");

    // Genera y envía el código. Respuesta SIEMPRE igual (anti-enumeración de correos).
    [EnableRateLimiting("auth")]
    [HttpPost("/forgot")]
    public async Task<IActionResult> Forgot([FromForm] string? email)
    {
        var mail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (_tenant.TenantId is Guid tid && !string.IsNullOrWhiteSpace(mail))
        {
            var user = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == mail && u.TenantId == tid);
            if (user is not null)
            {
                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                user.ResetCodeHash = PasswordHasher.Hash(code);
                user.ResetCodeExpiresUtc = DateTime.UtcNow.AddMinutes(CodeMinutes);
                user.ResetCodeAttempts = 0;
                await _db.SaveChangesAsync();

                var tenantName = await CurrentTenantNameAsync();
                // Envío en background (fire-and-forget con su propio scope): la respuesta HTTP
                // no espera el correo, así el timing es igual exista o no el usuario.
                var to = user.Email;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        var email = scope.ServiceProvider.GetRequiredService<Valentinos.Application.Notifications.IEmailSender>();
                        await email.SendAsync(new[] { to }, AuthEmail.Subject(tenantName),
                            AuthEmail.RenderResetCode(tenantName, code, CodeMinutes), isHtml: true);
                        _logger.LogInformation("🔑 Código de reset enviado a {Email}", to);
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Fallo al enviar código de reset"); }
                });
            }
        }
        // Siempre a la página de reset (no revelamos si el correo existe).
        return Redirect($"/reset?e={Uri.EscapeDataString(mail)}");
    }

    [HttpGet("/reset")]
    public IActionResult ResetPage([FromQuery] string? e, [FromQuery] string? error) =>
        Content(ResetHtml(e ?? "", error), "text/html; charset=utf-8");

    [EnableRateLimiting("auth")]
    [HttpPost("/reset")]
    public async Task<IActionResult> Reset([FromForm] string? email, [FromForm] string? code, [FromForm] string? password)
    {
        var mail = (email ?? string.Empty).Trim().ToLowerInvariant();
        var codeIn = (code ?? string.Empty).Trim();
        var pass = password ?? string.Empty;
        var back = $"/reset?e={Uri.EscapeDataString(mail)}";

        if (pass.Length < 6) return Redirect($"{back}&error=weak");

        if (_tenant.TenantId is not Guid tid) return Redirect($"{back}&error=code");
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == mail && u.TenantId == tid);

        if (user?.ResetCodeHash is null || user.ResetCodeExpiresUtc is null
            || user.ResetCodeExpiresUtc < DateTime.UtcNow || user.ResetCodeAttempts >= MaxAttempts)
            return Redirect($"{back}&error=code");

        if (!PasswordHasher.Verify(codeIn, user.ResetCodeHash))
        {
            user.ResetCodeAttempts++;
            await _db.SaveChangesAsync();
            return Redirect($"{back}&error=code");
        }

        // Código válido: fija la nueva contraseña, invalida el código y regenera el sello
        // de seguridad (invalida cualquier sesión/cookie anterior de este usuario).
        user.PasswordHash = PasswordHasher.Hash(pass);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ResetCodeHash = null;
        user.ResetCodeExpiresUtc = null;
        user.ResetCodeAttempts = 0;
        await _db.SaveChangesAsync();
        _logger.LogInformation("✅ Contraseña restablecida para {Email}", user.Email);
        return Redirect("/login?reset=1");
    }

    private async Task<string> CurrentTenantNameAsync()
    {
        if (_tenant.TenantId is not Guid tid) return "ValentiSoft";
        var t = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == tid);
        return t?.Nombre ?? "ValentiSoft";
    }

    private static string LoginHtml(string tenantNombre, string? error, bool resetOk = false)
    {
        var tenantH = WebUtility.HtmlEncode(tenantNombre);
        var errBox = string.IsNullOrEmpty(error) ? "" :
            @"<div class=""err"">Invalid email or password.</div>";
        if (resetOk) errBox += @"<div class=""ok"">Password updated. You can sign in now.</div>";
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
  .logo {{ width:64px; height:64px; border-radius:16px; background:#fff url(/images/brand/mastercorp-logo.png) no-repeat 50%/70%;
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
  .ok {{ background:#eaf7ee; border:1px solid #16a34a; color:#166534; padding:11px 12px; border-radius:10px; font-size:14px; margin-bottom:14px; }}
  .forgot {{ text-align:right; margin-top:10px; }} .forgot a {{ color:#1560A8; font-size:13px; text-decoration:none; font-weight:600; }}
  .forgot a:hover {{ text-decoration:underline; }}
</style></head>
<body>
  <form class=""card"" method=""post"" action=""/login"">
    <div class=""brand"">
      <div class=""logo"" role=""img"" aria-label=""ValentiSoft""></div>
      <h1>Sign in</h1>
      <div class=""sub"">{tenantH}</div>
    </div>
    {errBox}
    <label for=""email"">Email</label>
    <input type=""email"" id=""email"" name=""email"" required autocomplete=""username"" placeholder=""you@company.com"">
    <label for=""password"">Password</label>
    <input type=""password"" id=""password"" name=""password"" required autocomplete=""current-password"">
    <div class=""forgot""><a href=""/forgot"">Forgot password?</a></div>
    <label class=""row""><input type=""checkbox"" name=""remember"" value=""1""> Keep me signed in</label>
    <button type=""submit"">Sign in</button>
  </form>
</body></html>";
    }

    // Estilos compartidos de las páginas de auth (card centrada, logo MasterCorp).
    private static string AuthShell(string tenantNombre, string title, string inner) =>
$@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>{WebUtility.HtmlEncode(title)} · {WebUtility.HtmlEncode(tenantNombre)}</title>
<link rel=""icon"" href=""/favicon.ico"" sizes=""any"">
<link rel=""icon"" type=""image/png"" href=""/favicon-32.png"">
<style>
  :root {{ color-scheme: light; }} * {{ box-sizing:border-box; }}
  body {{ margin:0; min-height:100vh; min-height:100dvh; display:flex; align-items:center; justify-content:center;
    background:#eef1f5; color:#334155; font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; padding:18px; }}
  .card {{ background:#fff; border:1px solid #e5e9f0; border-radius:16px; padding:28px 24px; width:100%; max-width:400px;
    box-shadow:0 12px 40px rgba(15,23,42,.10); }}
  .brand {{ display:flex; flex-direction:column; align-items:center; text-align:center; margin-bottom:20px; }}
  .logo {{ width:64px; height:64px; border-radius:16px; background:#fff url(/images/brand/mastercorp-logo.png) no-repeat 50%/70%;
    border:1px solid #e5e9f0; box-shadow:0 8px 22px rgba(15,23,42,.10); margin-bottom:12px; }}
  h1 {{ font-size:20px; margin:0; color:#1560A8; }}
  .sub {{ color:#64748b; font-size:13px; margin-top:6px; line-height:1.5; }}
  label {{ display:block; font-size:13px; font-weight:600; color:#475569; margin:14px 0 6px; }}
  input {{ width:100%; padding:12px; border-radius:10px; border:1px solid #cbd5e1; font-size:16px; color:#1f2937; }}
  input:focus {{ outline:none; border-color:#1560A8; box-shadow:0 0 0 3px rgba(21,96,168,.15); }}
  input::placeholder {{ color:#cbd5e1; }}
  input.code {{ text-align:center; letter-spacing:8px; font-size:22px; font-family:ui-monospace,monospace; }}
  button {{ width:100%; margin-top:20px; padding:13px; border:0; border-radius:10px; background:#1560A8; color:#fff;
    font-weight:700; font-size:15px; cursor:pointer; }}
  button:hover {{ background:#0f4c85; }}
  .err {{ background:#fef2f2; border:1px solid #fecaca; color:#b91c1c; padding:11px 12px; border-radius:10px; font-size:14px; margin-bottom:14px; }}
  .ok {{ background:#eaf7ee; border:1px solid #16a34a; color:#166534; padding:11px 12px; border-radius:10px; font-size:14px; margin-bottom:14px; }}
  .back {{ text-align:center; margin-top:16px; }} .back a {{ color:#1560A8; font-size:13px; text-decoration:none; font-weight:600; }}
</style></head>
<body>{inner}</body></html>";

    private string ForgotHtml(bool sent)
    {
        var tenant = WebUtility.HtmlEncode("");
        var inner =
$@"  <form class=""card"" method=""post"" action=""/forgot"">
    <div class=""brand"">
      <div class=""logo"" role=""img""></div>
      <h1>Reset password</h1>
      <div class=""sub"">Enter your account email and we'll send you a 6-digit verification code.</div>
    </div>
    <label for=""email"">Email</label>
    <input type=""email"" id=""email"" name=""email"" required autocomplete=""username"" placeholder=""you@company.com"">
    <button type=""submit"">Send code</button>
    <div class=""back""><a href=""/login"">← Back to sign in</a></div>
  </form>";
        return AuthShell("MasterCorp", "Reset password", inner);
    }

    private string ResetHtml(string email, string? error)
    {
        var emailH = WebUtility.HtmlEncode(email);
        var box = error switch
        {
            "code" => @"<div class=""err"">Invalid or expired code. Request a new one if needed.</div>",
            "weak" => @"<div class=""err"">Password must be at least 6 characters.</div>",
            _ => @"<div class=""ok"">If that email has an account, we sent a 6-digit code. Check your inbox.</div>"
        };
        var inner =
$@"  <form class=""card"" method=""post"" action=""/reset"">
    <div class=""brand"">
      <div class=""logo"" role=""img""></div>
      <h1>Enter code</h1>
      <div class=""sub"">Type the 6-digit code we emailed you, then set a new password.</div>
    </div>
    {box}
    <input type=""hidden"" name=""email"" value=""{emailH}"">
    <label for=""code"">Verification code</label>
    <input class=""code"" type=""text"" id=""code"" name=""code"" inputmode=""numeric"" maxlength=""6"" pattern=""[0-9]{{6}}"" required placeholder=""000000"">
    <label for=""password"">New password</label>
    <input type=""password"" id=""password"" name=""password"" required autocomplete=""new-password"" minlength=""6"">
    <button type=""submit"">Update password</button>
    <div class=""back""><a href=""/forgot"">Didn't get a code? Try again</a></div>
  </form>";
        return AuthShell("MasterCorp", "Enter code", inner);
    }
}
