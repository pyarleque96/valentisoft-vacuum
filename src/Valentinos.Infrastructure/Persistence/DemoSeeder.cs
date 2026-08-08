using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Entities;
using Valentinos.Domain.Enums;
using Valentinos.Infrastructure.Auth;

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

        // ---- Sites del tenant. La identidad estable es el SLUG (el Code puede
        // cambiar: el placeholder "002" se convirtió en el site real "127 HCC"). ----
        var seedSites = new[]
        {
            (Code: "069", Slug: "XjUS3", Emails: (string?)"ramces.rodriguez@mastercorp.com"),
            (Code: "127 HCC", Slug: "Kp7Qm", Emails: (string?)"learsy.betancourt@mastercorp.com, carlos.reyes@mastercorp.com, gilberto.espinoza@mastercorp.com"),
            (Code: "200BOY", Slug: "Vn5Tq", Emails: (string?)"sherri.clapper@mastercorp.com, jodi.hazel@mastercorp.com, theresa.schneider@mastercorp.com, marianne.watkins@mastercorp.com"),
            // Los placeholders 003 y 004 se eliminaron: no se siembran más. Si se
            // vuelven a agregar aquí, reaparecerían en el panel al siguiente arranque.
        };

        // Migración de slugs viejos con prefijo "site-" -> sin prefijo (idempotente).
        foreach (var old in await db.Sites.Where(x => x.Slug.StartsWith("site-")).ToListAsync())
            old.Slug = old.Slug.Substring(5);
        await db.SaveChangesAsync();

        foreach (var s in seedSites)
        {
            var existing = await db.Sites.FirstOrDefaultAsync(x => x.Slug == s.Slug);
            if (existing is null)
            {
                db.Sites.Add(new Site { Code = s.Code, Slug = s.Slug, Emails = s.Emails });
                continue;
            }
            // El placeholder "002" pasa a ser el site real "127 HCC". Si el code ya fue
            // cambiado a otra cosa desde el panel, no se toca.
            if (existing.Code == "002" && s.Code == "127 HCC")
            {
                existing.Code = s.Code;
                // Los emails solo se siembran EN ESTE MOMENTO de la transición (una sola
                // vez) y solo si están vacíos: si el admin los borró explícitamente
                // después, un reinicio del seeder no debe resucitarlos.
                if (string.IsNullOrWhiteSpace(existing.Emails) && !string.IsNullOrWhiteSpace(s.Emails))
                    existing.Emails = s.Emails;
            }
        }
        await db.SaveChangesAsync();

        var site069 = await db.Sites.FirstAsync(x => x.Slug == "XjUS3");
        var site127 = await db.Sites.FirstAsync(x => x.Slug == "Kp7Qm");
        var site200 = await db.Sites.FirstAsync(x => x.Slug == "Vn5Tq");

        // ---- Usuarios admin del tenant. Password hasheada (PBKDF2). ----
        // NOTA: credenciales semilla de demo; cambiar en un entorno real.
        var seedAdmins = new[]
        {
            (Email: "christopher.davey@mastercorp.com", Name: "Chris David", Pass: "ChrisD123*"),
            (Email: "ramces.rodriguez@mastercorp.com", Name: "Ramces Rodriguez", Pass: "RamcesR123*"),
        };
        foreach (var a in seedAdmins)
        {
            if (!await db.Users.AnyAsync(u => u.Email == a.Email))
            {
                db.Users.Add(new User
                {
                    Email = a.Email, DisplayName = a.Name, Role = "admin",
                    PasswordHash = PasswordHasher.Hash(a.Pass)
                });
            }
        }
        await db.SaveChangesAsync();

        // Backfill: usuarios sin SecurityStamp (rows previas a la migración) reciben uno.
        foreach (var u in await db.Users.Where(x => x.SecurityStamp == null || x.SecurityStamp == "").ToListAsync())
            u.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        // Vacuums por site. La numeración es POR SITE desde la migración
        // SiteScopedVacuumCode, así que NO se usa el contador global del AssetType
        // (que es compartido entre sites) ni un conteo global de assets.
        await SeedVacuumsAsync(db, tipo.Id, site069.Id, Vacuums069);
        await SeedVacuumsAsync(db, tipo.Id, site127.Id, Vacuums127);
        await SeedVacuumsAsync(db, tipo.Id, site200.Id, Vacuums200);

        // Backfill: vacuums existentes sin site (SiteId vacío) -> Site 069.
        var huerfanos = await db.Assets.Where(a => a.SiteId == Guid.Empty).ToListAsync();
        if (huerfanos.Count > 0)
        {
            foreach (var a in huerfanos) a.SiteId = site069.Id;
            await db.SaveChangesAsync();
        }

        // Backfill: empleados existentes sin site (SiteId vacío) -> Site 069. Si el
        // backfill de la migración EmployeeSiteScoped no matcheó (p. ej. el Code del
        // site 069 fue editado desde el panel antes de correr la migración), esto evita
        // que SeedEmployeesAsync los duplique al no encontrarlos ya asignados.
        var huerfanosEmpleados = await db.Employees.Where(e => e.SiteId == Guid.Empty).ToListAsync();
        if (huerfanosEmpleados.Count > 0)
        {
            foreach (var e in huerfanosEmpleados) e.SiteId = site069.Id;
            await db.SaveChangesAsync();
        }

        // Empleados POR SITE (para el autocompletar del formulario de cada site).
        await SeedEmployeesAsync(db, site069.Id, Empleados069);
        await SeedEmployeesAsync(db, site127.Id, Empleados127);
        await SeedEmployeesAsync(db, site200.Id, Empleados200);

        // Sembrado de reportes fake DESACTIVADO (producción arranca limpio). Poner en true
        // solo si se quiere volver a poblar el dashboard con data de demo.
        const bool seedFakeReports = false;

        // Data FAKE de check-ins por activo del último mes (para la página de KPIs).
        // Los estados "no disponible" ya NO viven aquí: tienen su propia tabla/flujo.
        if (seedFakeReports && !await db.StatusCheckins.AnyAsync())
        {
            var codes = await db.Assets.Select(a => a.Codigo).OrderBy(c => c).ToListAsync();
            var faults = new[] { "Low suction", "Damaged cable", "Won't turn on", "Broken wheel", "Overheating", "Strange noise" };
            var weights = new[] { ("operational", 68), ("AMedias", 20), ("NoFunciona", 12) };
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
                        EmployeeName = Empleados069[rnd.Next(Empleados069.Length)],
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

        // Data FAKE de reportes de "equipo no disponible" (QR fijo del cuarto de housekeeping).
        if (seedFakeReports && !await db.UnavailableReports.AnyAsync())
        {
            var reasons = new[]
            {
                "No hay aspiradoras disponibles en el piso 3",
                "Todas las aspiradoras están en uso",
                "Falta una aspiradora en el clóset de suministros",
                "Aspiradora prestada a otro turno",
                "No quedan aspiradoras operativas para el turno noche"
            };
            var rnd2 = new Random(20260729);
            var ureps = new List<UnavailableReport>();
            for (var d = 0; d < 30; d++)
            {
                var day = DateTime.Now.Date.AddDays(-d);
                var perDay = rnd2.Next(0, 4); // algunos días sin reportes
                for (var k = 0; k < perDay; k++)
                {
                    ureps.Add(new UnavailableReport
                    {
                        EmployeeName = Empleados069[rnd2.Next(Empleados069.Length)],
                        EquipmentType = "Vacuum",
                        Nota = rnd2.Next(4) == 0 ? null : reasons[rnd2.Next(reasons.Length)],
                        CreatedAt = day.AddHours(rnd2.Next(6, 20)).AddMinutes(rnd2.Next(60))
                    });
                }
            }
            db.UnavailableReports.AddRange(ureps);
            await db.SaveChangesAsync();
        }
    }

    // Alta idempotente de vacuums de un site: solo agrega los códigos que faltan.
    private static async Task SeedVacuumsAsync(AppDbContext db, Guid assetTypeId, Guid siteId, string[] codigos)
    {
        var existentes = await db.Assets.Where(a => a.SiteId == siteId).Select(a => a.Codigo).ToListAsync();
        foreach (var codigo in codigos)
        {
            if (existentes.Contains(codigo)) continue;
            db.Assets.Add(new Asset
            {
                AssetTypeId = assetTypeId,
                SiteId = siteId,
                Codigo = codigo,
                Estado = AssetEstado.Activo
            });
        }
        await db.SaveChangesAsync();
    }

    // Alta idempotente de empleados de un site: solo agrega los nombres que faltan.
    private static async Task SeedEmployeesAsync(AppDbContext db, Guid siteId, string[] nombres)
    {
        var existentes = await db.Employees.Where(e => e.SiteId == siteId).Select(e => e.Nombre).ToListAsync();
        foreach (var nombre in nombres)
        {
            if (existentes.Contains(nombre)) continue;
            db.Employees.Add(new Employee { SiteId = siteId, Nombre = nombre });
        }
        await db.SaveChangesAsync();
    }

    // Site 069: 16 vacuums numerados.
    private static readonly string[] Vacuums069 =
        Enumerable.Range(1, 16).Select(i => $"VAC-{i:D3}").ToArray();

    // Site 127 HCC: 11 numerados + los 2 janitorial con nombre propio.
    private static readonly string[] Vacuums127 =
        Enumerable.Range(1, 11).Select(i => $"VAC-{i:D3}")
                  .Concat(new[] { "VAC-TIMESQUARE", "VAC-FRONTDESK" })
                  .ToArray();

    // Site 200BOY (Mountain Run at Boyne): 28 vacuums numerados.
    private static readonly string[] Vacuums200 =
        Enumerable.Range(1, 28).Select(i => $"VAC-{i:D3}").ToArray();

    private static readonly string[] Empleados200 =
    {
        "Alfaro Flores, Kevin E",
        "Carcamo Osorio, Lorena",
        "Cifuentes Zecena, Lisbett V",
        "Cortez Orellana, Maria",
        "Cuadra Pineda, Erika",
        "Estrada, Jennifer Noemy",
        "Garcia, Yesenia Y",
        "Lapointe, Melisa",
        "Lopez Carranza, Elvin",
        "Marroquin Marroquin, Sirli Y",
        "Martinez, Monica",
        "Perez Contreras, Vilma E",
        "Ruiz Mancia, Iliana",
        "Strickler, Dyllan"
    };

    private static readonly string[] Empleados127 =
    {
        "Gonzalez Guerra, Yanet",
        "Jeronimo, Brissman",
        "Martea, Lidia",
        "Pacheco, Wendy",
        "Zdor, Galina"
    };

    private static readonly string[] Empleados069 =
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
