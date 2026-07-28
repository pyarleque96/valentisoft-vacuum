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

        // Setting de la org: email por reporte APAGADO por ahora (se usa la página de KPIs).
        // El correo destino queda configurado por si se reactiva.
        if (tenant.EmailNotificationsEnabled || tenant.NotificationEmails != notificationEmail)
        {
            tenant.EmailNotificationsEnabled = false;
            if (!string.IsNullOrWhiteSpace(notificationEmail))
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

        // Sembrar hasta 12 aspiradoras (VAC-001..VAC-012).
        var nAssets = await db.Assets.CountAsync(a => a.AssetTypeId == tipo.Id);
        for (var i = nAssets; i < 12; i++)
        {
            tipo.CorrelativoActual += 1;
            db.Assets.Add(new Asset
            {
                AssetTypeId = tipo.Id,
                Codigo = $"{tipo.Prefijo}-{tipo.CorrelativoActual:D3}",
                Estado = AssetEstado.Activo
            });
        }
        await db.SaveChangesAsync();

        // Empleados de MasterCorp (para el autocompletar del formulario).
        if (!await db.Employees.AnyAsync())
        {
            foreach (var nombre in Empleados)
                db.Employees.Add(new Employee { Nombre = nombre });
            await db.SaveChangesAsync();
        }

        // Data FAKE de check-ins del último mes (para la página de KPIs).
        if (!await db.StatusCheckins.AnyAsync())
        {
            var codes = await db.Assets.Select(a => a.Codigo).OrderBy(c => c).ToListAsync();
            var faults = new[] { "Low suction", "Damaged cable", "Won't turn on", "Broken wheel", "Overheating", "Strange noise" };
            var weights = new[] { ("operational", 66), ("AMedias", 12), ("NoFunciona", 7), ("unavailable", 15) };
            var totalW = weights.Sum(w => w.Item2);
            var rnd = new Random(20260728);
            var list = new List<StatusCheckin>();

            for (var d = 0; d < 30; d++)
            {
                var day = DateTime.Now.Date.AddDays(-d);
                var perDay = rnd.Next(8, 16);
                for (var k = 0; k < perDay; k++)
                {
                    // Selección ponderada de estado.
                    var pick = rnd.Next(totalW);
                    var acc = 0; var est = "operational";
                    foreach (var (key, w) in weights) { acc += w; if (pick < acc) { est = key; break; } }

                    var isProblem = est is "AMedias" or "NoFunciona";
                    // Sesgo: VAC-007 falla más.
                    var code = (isProblem && rnd.Next(3) == 0) ? "VAC-007" : codes[rnd.Next(codes.Count)];

                    list.Add(new StatusCheckin
                    {
                        EmployeeName = Empleados[rnd.Next(Empleados.Length)],
                        AssetCodigo = code,
                        EstadoKey = est,
                        Nota = isProblem ? faults[rnd.Next(faults.Length)] : null,
                        CreatedAt = day.AddHours(rnd.Next(6, 20)).AddMinutes(rnd.Next(60))
                    });
                }
            }
            db.StatusCheckins.AddRange(list);
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
