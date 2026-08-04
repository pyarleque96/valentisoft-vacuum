using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Valentinos.Api.Multitenancy;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Infrastructure;
using Valentinos.Infrastructure.Notifications;
using Valentinos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<ITenantContext, TenantContext>();
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'Default'.");
builder.Services.AddInfrastructure(connectionString, builder.Configuration);

// En desarrollo, si NO hay SMTP configurado, las alertas se imprimen en la consola.
// Si Smtp:Enabled=true (config/user-secrets), se usa el SmtpEmailSender real.
var smtpEnabled = builder.Configuration.GetValue<bool>("Smtp:Enabled");
if (builder.Environment.IsDevelopment() && !smtpEnabled)
    builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();

// Servicio de envío de reportes + scheduler diario (10:00 hora local).
builder.Services.AddScoped<Valentinos.Api.Reports.ReportEmailer>();
builder.Services.AddHostedService<Valentinos.Api.Reports.DailyReportScheduler>();

// Autenticación por cookie para el panel admin.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(365); // sesión larga (deslizante); no expira en uso normal
        o.SlidingExpiration = true;
        o.Cookie.Name = "valentisoft.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // Forzar redirect 302 al login (evita 401 en navegaciones de página).
        o.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx => { ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask; },
            OnRedirectToAccessDenied = ctx => { ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask; },
            // Valida el sello de seguridad en cada request: si la contraseña cambió
            // (stamp regenerado), las cookies emitidas antes quedan inválidas.
            OnValidatePrincipal = async ctx =>
            {
                var stamp = ctx.Principal?.FindFirst("stamp")?.Value;
                var uid = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (stamp is null || uid is null || !Guid.TryParse(uid, out var userId))
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var current = await db.Users.IgnoreQueryFilters()
                    .Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstOrDefaultAsync();
                if (current is null || current != stamp)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-reports", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    // Endpoints de autenticación (login / forgot / reset): límite por IP para frenar
    // fuerza bruta y email-bombing.
    options.AddPolicy("auth", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

// Ruta del logo para incrustarlo (cid) en los correos.
var smtpOpts = app.Services.GetRequiredService<SmtpOptions>();
if (string.IsNullOrWhiteSpace(smtpOpts.InlineLogoPath) && !string.IsNullOrWhiteSpace(app.Environment.WebRootPath))
    smtpOpts.InlineLogoPath = Path.Combine(app.Environment.WebRootPath, "images", "brand", "valentinos-v.jpg");

// Detrás del túnel de Cloudflare, respetar X-Forwarded-Proto/Host (para que las
// cookies Secure y los redirects usen https y el host público, no localhost).
var fwd = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost
};
fwd.KnownNetworks.Clear(); fwd.KnownProxies.Clear(); // el túnel local es de confianza
app.UseForwardedHeaders(fwd);

app.UseStaticFiles(); // sirve wwwroot (banderas de idioma de la demo)
// El middleware de tenant va ANTES de UseRouting: reescribe la ruta (inyecta el slug
// del subdominio) antes del match de endpoints. UseRouting explícito evita que el
// framework lo inserte automáticamente al inicio del pipeline.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

// Migración + seed al arrancar (excepto en entorno de tests)
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);

    // Datos de demo (solo Development): tipo Aspiradora (VAC) + activo VAC-001.
    if (app.Environment.IsDevelopment())
    {
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        await DemoSeeder.SeedAsync(db, tenantContext,
            "christopher.strait@mastercorp.com,christopher.davey@mastercorp.com");
    }
}

app.Run();

public partial class Program { }
