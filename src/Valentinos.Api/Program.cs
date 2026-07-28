using System.Threading.RateLimiting;
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
});

var app = builder.Build();

// Ruta del logo para incrustarlo (cid) en los correos.
var smtpOpts = app.Services.GetRequiredService<SmtpOptions>();
if (string.IsNullOrWhiteSpace(smtpOpts.InlineLogoPath) && !string.IsNullOrWhiteSpace(app.Environment.WebRootPath))
    smtpOpts.InlineLogoPath = Path.Combine(app.Environment.WebRootPath, "images", "brand", "valentinos-v.jpg");

app.UseStaticFiles(); // sirve wwwroot (banderas de idioma de la demo)
app.UseMiddleware<TenantResolutionMiddleware>();
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
        await DemoSeeder.SeedAsync(db, tenantContext, "ramcesdrag@gmail.com");
    }
}

app.Run();

public partial class Program { }
