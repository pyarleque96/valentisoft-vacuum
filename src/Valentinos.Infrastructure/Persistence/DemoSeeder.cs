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
            tipo = AssetType.Create("Vacuum", "VAC");
            db.AssetTypes.Add(tipo);
            await db.SaveChangesAsync();
        }
        else if (tipo.Nombre != "Vacuum") // renombrar el tipo a inglés
        {
            tipo.Nombre = "Vacuum";
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

        // Empleados de MasterCorp (para el autocompletar del formulario).
        if (!await db.Employees.AnyAsync())
        {
            foreach (var nombre in Empleados)
                db.Employees.Add(new Employee { Nombre = nombre });
            await db.SaveChangesAsync();
        }
    }

    private static readonly string[] Empleados =
    {
        "Castillo Carbajal, Leticia J",
        "Ffrench, Dothlyn",
        "Gutierrez Flores, Helen A",
        "Gutierrez, Damaris",
        "Hernandez Quijada, Rodrigo Salvador",
        "Jacobo Figueroa, Maira",
        "Jeronimo Gonzalez, Jennifer",
        "Lalin Marin, Heidy N",
        "Lopez Solano, Delma",
        "Martinez de Delgado, Mayra",
        "Rivera Chavez, Deysi",
        "Rodriguez Reyes, Maria",
        "Rosales Solis, Sergio",
        "Ruano Arroyo, Yecsenia",
        "Rueda Beltran, Diana",
        "Wilson, Kenroy"
    };
}
