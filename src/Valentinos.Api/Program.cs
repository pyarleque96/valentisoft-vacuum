using Microsoft.EntityFrameworkCore;
using Valentinos.Api.Multitenancy;
using Valentinos.Application.Abstractions;
using Valentinos.Infrastructure;
using Valentinos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Default")!);

var app = builder.Build();

app.UseMiddleware<TenantResolutionMiddleware>();
app.MapControllers();

// Migración + seed al arrancar (excepto en entorno de tests)
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.Run();

public partial class Program { }
