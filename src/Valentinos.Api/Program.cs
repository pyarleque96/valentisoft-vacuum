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

// En desarrollo/demo, las alertas de email se imprimen en la consola en vez de
// enviarse por SMTP (sobrescribe el IEmailSender registrado por AddInfrastructure).
if (builder.Environment.IsDevelopment())
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
        await DemoSeeder.SeedAsync(db, tenantContext, "pedroyarleque96@gmail.com");
    }
}

app.Run();

public partial class Program { }
