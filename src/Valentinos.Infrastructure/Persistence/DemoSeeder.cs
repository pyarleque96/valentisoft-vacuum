using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Entities;
using Valentinos.Domain.Enums;

namespace Valentinos.Infrastructure.Persistence;

// Siembra de datos SOLO para la demo (Development): asegura que MasterCorp tenga
// un tipo Aspiradora (VAC) y un activo VAC-001, y un email destino para las
// alertas. Idempotente. Requiere un ITenantContext para poder escribir las
// entidades ITenantOwned (el write-path exige tenant en contexto).
public static class DemoSeeder
{
    public static async Task SeedAsync(AppDbContext db, ITenantContext tenantContext, string? notificationEmail = null)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == "mastercorp");
        if (tenant is null) return; // DbSeeder ya debería haber creado MasterCorp

        // Email destino para ver la alerta en la demo (si se provee).
        if (!string.IsNullOrWhiteSpace(notificationEmail) && tenant.NotificationEmails != notificationEmail)
        {
            tenant.EmailNotificationsEnabled = true;
            tenant.NotificationEmails = notificationEmail;
            await db.SaveChangesAsync();
        }

        // A partir de aquí escribimos entidades del tenant: fijamos el contexto.
        tenantContext.Set(tenant.Id);

        var tipo = await db.AssetTypes.FirstOrDefaultAsync(t => t.Prefijo == "VAC");
        if (tipo is null)
        {
            tipo = AssetType.Create("Aspiradora", "VAC");
            db.AssetTypes.Add(tipo);
            await db.SaveChangesAsync();
        }

        var existeActivo = await db.Assets.AnyAsync(a => a.AssetTypeId == tipo.Id);
        if (!existeActivo)
        {
            tipo.CorrelativoActual += 1; // -> 1
            db.Assets.Add(new Asset
            {
                AssetTypeId = tipo.Id,
                Codigo = $"{tipo.Prefijo}-{tipo.CorrelativoActual:D3}", // VAC-001
                Estado = AssetEstado.Activo,
                Ubicacion = "Piso 1"
            });
            await db.SaveChangesAsync();
        }
    }
}
