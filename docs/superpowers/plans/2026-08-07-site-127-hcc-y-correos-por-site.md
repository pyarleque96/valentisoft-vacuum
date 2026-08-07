# Site 127 HCC + correos diarios por site — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar el site real `127 HCC` (13 vacuums, 5 empleados, 3 destinatarios) sobre el placeholder `002`, y hacer que cada site reciba a las 10:00 su propio correo diario con solo su data, además del consolidado que ya existe.

**Architecture:** Cinco cambios encadenados sobre una app ASP.NET Core con Clean Architecture (Domain / Application / Infrastructure / Api). Dos migraciones de EF Core (empleados scopeados por site; renombre de `Site.CcEmails` a `Site.Emails`), una refactorización de `ReportEmailer` que separa "armar el KPI de un site" de "a quién enviarlo", un orquestador `DailyReportRunner` extraído del `BackgroundService` para poder probarlo sin temporizadores, y la siembra de datos en `DemoSeeder`.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core 10 (SQL Server en runtime, InMemory en tests), xUnit, `WebApplicationFactory<Program>` para tests de endpoint, SkiaSharp para el PDF de KPIs.

## Global Constraints

- **NO SE ENVÍA NINGÚN CORREO, NI DE PRUEBA.** En los user-secrets del proyecto `Smtp:Enabled = true`. Está prohibido: arrancar la app con `ASPNETCORE_ENVIRONMENT=Development` y dejarla viva (el `DailyReportScheduler` dispara a las 10:00), e invocar `POST /admin/reports/sample`. Los tests corren con `UseEnvironment("Testing")`, que no carga user-secrets, por lo que `Smtp:Enabled` queda en `false` y `SmtpEmailSender.SendAsync` retorna antes de abrir conexión — verificar que cualquier test nuevo mantenga esa condición o use un `IEmailSender` falso.
- Código de negocio del site nuevo: exactamente `127 HCC` (con espacio).
- Slug del site nuevo: `Kp7Qm` (el que ya tiene el placeholder `002`; **no se genera uno nuevo**).
- Destinatarios del site 127 HCC, en este orden y separados por `", "`:
  `learsy.betancourt@mastercorp.com, carlos.reyes@mastercorp.com, gilberto.espinoza@mastercorp.com`
- Destinatario del correo consolidado: `christopher.davey@mastercorp.com` (se quita `christopher.strait@mastercorp.com`). El CC (`ramces.rodriguez@mastercorp.com`) ya está en `Smtp:Cc` en user-secrets: **no se toca**.
- Códigos de los 13 vacuums del 127 HCC: `VAC-001` … `VAC-011`, `VAC-TIMESQUARE`, `VAC-FRONTDESK`.
- Nombres de los 5 empleados del 127 HCC, en formato `Apellidos, Nombre`:
  `Gonzalez Guerra, Yanet` · `Jeronimo, Brissman` · `Martea, Lidia` · `Pacheco, Wendy` · `Zdor, Galina`
- Correo por site: destinatarios en TO, **sin CC** (`includeConfiguredCc: false`).
- Correo consolidado: **con CC** (`includeConfiguredCc: true`), comportamiento actual.
- Site sin actividad en el periodo o sin emails configurados → no se envía su correo.
- Los comentarios del código se escriben en español, como el resto del repo. Los textos visibles al usuario (labels del panel, asuntos de correo) en inglés.
- Comando de tests: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`

## File Structure

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `src/Valentinos.Domain/Entities/Employee.cs` | agrega `SiteId` | 1 |
| `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs` | índice de `Employee`; largo de `Site.Emails` | 1, 2 |
| `src/Valentinos.Infrastructure/Persistence/Migrations/*_EmployeeSiteScoped.cs` | columna + backfill SQL al site `069` | 1 |
| `src/Valentinos.Api/Controllers/DemoController.cs` | endpoint de empleados filtra por site | 1 |
| `src/Valentinos.Domain/Entities/Site.cs` | `CcEmails` → `Emails` | 2 |
| `src/Valentinos.Infrastructure/Persistence/Migrations/*_SiteEmailsRename.cs` | `RenameColumn` | 2 |
| `src/Valentinos.Api/Controllers/AdminController.cs` | usos del campo + labels `Emails` | 2 |
| `src/Valentinos.Api/Reports/ReportEmailer.cs` | arma KPI por site; envía por-site y consolidado | 3 |
| `src/Valentinos.Api/Reports/DailyReportRunner.cs` (nuevo) | orquesta el envío diario, testeable sin temporizador | 4 |
| `src/Valentinos.Api/Reports/DailyReportScheduler.cs` | solo el temporizador; delega en el runner | 4 |
| `src/Valentinos.Api/Program.cs` | registra el runner; destinatario del consolidado | 4, 5 |
| `src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs` | site 127 HCC, vacuums, empleados, fix del 069 | 5 |
| `tests/Valentinos.Tests/Api/EmployeesEndpointTests.cs` (nuevo) | aislamiento de empleados por site | 1 |
| `tests/Valentinos.Tests/Reports/ReportEmailerTests.cs` (nuevo) | destinatarios, CC y aislamiento de data | 3 |
| `tests/Valentinos.Tests/Reports/DailyReportRunnerTests.cs` (nuevo) | orden y tolerancia a fallos del envío diario | 4 |
| `tests/Valentinos.Tests/Persistence/DemoSeederTests.cs` (nuevo) | siembra idempotente del 127 HCC | 5 |

Las tareas van en orden: 3 depende de 2 (usa `Site.Emails`), 4 depende de 3, y 5 depende de 1 y 2.

---

### Task 1: Empleados scopeados por site

**Files:**
- Modify: `src/Valentinos.Domain/Entities/Employee.cs`
- Modify: `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs:61-65`
- Create: `src/Valentinos.Infrastructure/Persistence/Migrations/<timestamp>_EmployeeSiteScoped.cs` (generado por `dotnet ef`)
- Modify: `src/Valentinos.Api/Controllers/DemoController.cs:376-389`
- Test: `tests/Valentinos.Tests/Api/EmployeesEndpointTests.cs`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `Employee.SiteId` (`Guid`), usado por la Task 5 al sembrar empleados.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/Valentinos.Tests/Api/EmployeesEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Api;

// El autocompletar del formulario público debe sugerir SOLO a los empleados del
// site cuyo QR se escaneó: el site 127 no puede ver los nombres del 069.
public class EmployeesEndpointTests : IClassFixture<EmployeesEndpointTests.Factory>
{
    private readonly Factory _factory;
    public EmployeesEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Employees_DevuelveSoloLosDelSiteConsultado()
    {
        var client = _factory.CreateClient();

        var uno = await client.GetFromJsonAsync<List<string>>("/api/public/SiteUno/employees");
        var dos = await client.GetFromJsonAsync<List<string>>("/api/public/SiteDos/employees");

        Assert.Equal(new[] { "Alfa, Ana" }, uno);
        Assert.Equal(new[] { "Beta, Bruno" }, dos);
    }

    [Fact]
    public async Task Employees_SiteInexistente_Devuelve404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/public/NoExiste/employees");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var descriptores = services.Where(d =>
                    d.ServiceType.FullName is not null &&
                    d.ServiceType.FullName.Contains(nameof(AppDbContext))).ToList();
                foreach (var d in descriptores) services.Remove(d);

                services.AddDbContext<AppDbContext>(o =>
                    o.UseInMemoryDatabase("EmployeesEndpointTests"));
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);

            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var tenantCtx = sp.GetRequiredService<ITenantContext>();

            var tenant = Tenant.Create("mastercorp", "MasterCorp");
            db.Tenants.Add(tenant);
            db.SaveChanges();

            // Site y Employee son ITenantOwned: el write-path exige tenant en contexto.
            tenantCtx.Set(tenant.Id);
            var uno = new Site { Code = "001", Slug = "SiteUno" };
            var dos = new Site { Code = "002", Slug = "SiteDos" };
            db.Sites.AddRange(uno, dos);
            db.SaveChanges();

            db.Employees.AddRange(
                new Employee { SiteId = uno.Id, Nombre = "Alfa, Ana" },
                new Employee { SiteId = dos.Id, Nombre = "Beta, Bruno" });
            db.SaveChanges();

            return host;
        }
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter EmployeesEndpointTests`
Expected: FAIL de compilación — `'Employee' no contiene una definición para 'SiteId'`.

- [ ] **Step 3: Agregar `SiteId` a la entidad**

Reemplazar el contenido de `src/Valentinos.Domain/Entities/Employee.cs`:

```csharp
using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class Employee : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }    // site al que pertenece (el autocompletar filtra por él)
    public string Nombre { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Actualizar el índice en `AppDbContext`**

En `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs`, reemplazar el bloque de `Employee`:

```csharp
        modelBuilder.Entity<Employee>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.SiteId, x.Nombre });
        });
```

- [ ] **Step 5: Filtrar el endpoint de empleados por site**

En `src/Valentinos.Api/Controllers/DemoController.cs`, dentro del método `Employees`, reemplazar la consulta:

```csharp
        var names = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenant.Id && e.SiteId == site.Id)
            .OrderBy(e => e.Nombre)
            .Select(e => e.Nombre)
            .ToListAsync();
        return Ok(names);
```

Y actualizar el comentario de la línea de arriba del `[HttpGet]` a:

```csharp
    // Lista de empleados DEL SITE (para el autocompletar del formulario).
```

- [ ] **Step 6: Correr el test y verificar que pasa**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter EmployeesEndpointTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Generar la migración**

Run:
```bash
dotnet ef migrations add EmployeeSiteScoped \
  -p src/Valentinos.Infrastructure/Valentinos.Infrastructure.csproj \
  -s src/Valentinos.Api/Valentinos.Api.csproj
```

Si `dotnet ef` no está instalado: `dotnet tool install --global dotnet-ef`.

Expected: se crean `Migrations/<timestamp>_EmployeeSiteScoped.cs` y su `.Designer.cs`, y se actualiza `AppDbContextModelSnapshot.cs`.

- [ ] **Step 8: Agregar el backfill al `Up()` de la migración**

Abrir el `<timestamp>_EmployeeSiteScoped.cs` recién generado y, **al final** del método `Up` (después del `AddColumn` y del `CreateIndex`), agregar:

```csharp
            // Backfill: los empleados existentes son todos del site 069. Va en la
            // migración y no en el seeder para que se aplique aunque DemoSeeder no corra.
            migrationBuilder.Sql(@"
                UPDATE e
                SET e.SiteId = s.Id
                FROM Employees e
                INNER JOIN Sites s ON s.TenantId = e.TenantId AND s.Code = '069'
                WHERE e.SiteId = '00000000-0000-0000-0000-000000000000';");
```

No hay que agregar nada al `Down()`: al borrar la columna el dato desaparece con ella.

- [ ] **Step 9: Correr toda la suite**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: PASS, sin regresiones.

- [ ] **Step 10: Commit**

```bash
git add src/Valentinos.Domain/Entities/Employee.cs \
        src/Valentinos.Infrastructure/Persistence/AppDbContext.cs \
        src/Valentinos.Infrastructure/Persistence/Migrations \
        src/Valentinos.Api/Controllers/DemoController.cs \
        tests/Valentinos.Tests/Api/EmployeesEndpointTests.cs
git commit -m "feat: empleados scopeados por site (autocompletar aislado por site)"
```

---

### Task 2: Renombrar `Site.CcEmails` a `Site.Emails` y las etiquetas del panel

Esta tarea es un renombre: **no lleva test nuevo**. Su verificación es el compilador, la suite existente en verde y un `grep` que confirme que ya no queda ninguna mención de "Recipients (CC)" en el código. Inventar un test para un renombre no aportaría nada.

**Files:**
- Modify: `src/Valentinos.Domain/Entities/Site.cs:13`
- Modify: `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs:71`
- Create: `src/Valentinos.Infrastructure/Persistence/Migrations/<timestamp>_SiteEmailsRename.cs`
- Modify: `src/Valentinos.Api/Controllers/AdminController.cs` (líneas 16, 80, 108, 192, 211, 221, 248-249, 287, 398)
- Modify: `src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs:65`

**Interfaces:**
- Consumes: nada de la Task 1.
- Produces: `Site.Emails` (`string?`), consumido por `ReportEmailer.SendSiteAsync` (Task 3), `DailyReportRunner` (Task 4) y `DemoSeeder` (Task 5).

- [ ] **Step 1: Renombrar la propiedad del dominio**

Reemplazar el contenido de `src/Valentinos.Domain/Entities/Site.cs`:

```csharp
using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

// Un "site" (sede) dentro de un tenant. MasterCorp tiene varios sites; cada site
// tiene sus propios vacuums, sus propios empleados, su QR fijo, y sus destinatarios
// de notificación. Se identifica en la URL por su Slug (p. ej. "XjUS3").
public class Site : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;   // código de negocio, p. ej. "069"
    public string Slug { get; set; } = string.Empty;   // segmento de URL aleatorio, p. ej. "XjUS3"
    public string? Emails { get; set; }                 // destinatarios (TO) del reporte del site, separados por coma
}
```

- [ ] **Step 2: Actualizar la configuración de EF**

En `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs`, en el bloque de `Site`, cambiar la línea:

```csharp
            e.Property(x => x.Emails).HasMaxLength(1000);
```

- [ ] **Step 3: Actualizar los usos en `AdminController`**

En `src/Valentinos.Api/Controllers/AdminController.cs` aplicar estos seis cambios:

1. Comentario de cabecera de la clase (línea 16), reemplazar por:
```csharp
// Panel de administración (solo admin autenticado): gestión de sites de un tenant,
// sus destinatarios de notificación y sus vacuums.
```

2. En `CreateSite`, cambiar `CcEmails = NormalizeEmails(cc)` por:
```csharp
            Emails = NormalizeEmails(cc)
```

3. En `SaveSite`, cambiar la asignación por:
```csharp
        site.Emails = NormalizeEmails(cc);
```

4. En `SitesHtml`, dentro del `foreach`, cambiar la línea de `cc` por:
```csharp
            var cc = string.IsNullOrWhiteSpace(s.Emails) ? "<span class=\"muted\">—</span>" : WebUtility.HtmlEncode(s.Emails);
```
y la cabecera de la tabla por:
```csharp
      <thead><tr><th>Site</th><th>Vacuums</th><th>Emails</th><th></th></tr></thead>
```
y el label del formulario oculto "New site" por:
```csharp
      <div class=""full""><label>Emails — comma separated</label><input name=""cc"" placeholder=""manager@company.com"" disabled></div>
```

5. En `SiteConfigHtml`, el bloque del campo por:
```csharp
      <div class=""full""><label>Emails — comma separated</label>
        <input id=""ccInput"" name=""cc"" value=""{WebUtility.HtmlEncode(site.Emails ?? "")}"" placeholder=""manager@company.com"">
        <div class=""fielderr"" id=""ccErr""></div></div>
```
y el comentario del bloque de validación JS (línea ~287) por:
```csharp
    // Validación de Emails: separados por coma; si hay error, muestra ejemplo.
```

6. En el CSS de `AdminLayout`, el comentario dentro del `@media (max-width:560px)` por:
```csharp
    /* En móvil ocultar la columna Emails del grid. */
```

El nombre del parámetro `cc` del formulario y la clase CSS `.cccell` se mantienen: son internos y renombrarlos rompería el `name=""cc""` del HTML sin ganancia.

- [ ] **Step 4: Actualizar `DemoSeeder`**

En `src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs`, en el `foreach (var s in seedSites)`, cambiar:

```csharp
                db.Sites.Add(new Site { Code = s.Code, Slug = s.Slug, Emails = s.Cc });
```

(La Task 5 reescribe este bloque entero; aquí solo se busca que compile.)

- [ ] **Step 5: Compilar**

Run: `dotnet build Valentinos.sln`
Expected: BUILD SUCCEEDED, cero errores.

- [ ] **Step 6: Generar la migración de renombre**

Run:
```bash
dotnet ef migrations add SiteEmailsRename \
  -p src/Valentinos.Infrastructure/Valentinos.Infrastructure.csproj \
  -s src/Valentinos.Api/Valentinos.Api.csproj
```

- [ ] **Step 7: Verificar que la migración renombra y no borra**

Abrir el `<timestamp>_SiteEmailsRename.cs` generado. El `Up()` **debe** contener un `RenameColumn`:

```csharp
            migrationBuilder.RenameColumn(
                name: "CcEmails",
                table: "Sites",
                newName: "Emails");
```

Si en cambio generó `DropColumn("CcEmails")` + `AddColumn("Emails")`, **eso perdería los emails ya configurados**. En ese caso reemplazar ambas llamadas a mano por el `RenameColumn` de arriba en el `Up()`, y por el inverso en el `Down()`:

```csharp
            migrationBuilder.RenameColumn(
                name: "Emails",
                table: "Sites",
                newName: "CcEmails");
```

- [ ] **Step 8: Verificar que no queda rastro de la etiqueta vieja**

Run: `git grep -n "Recipients (CC)" -- src tests`
Expected: sin resultados (exit code 1).

Run: `git grep -n "CcEmails" -- src/Valentinos.Domain src/Valentinos.Api src/Valentinos.Infrastructure/Persistence/AppDbContext.cs src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs`
Expected: sin resultados. (Las migraciones viejas **sí** siguen mencionando `CcEmails`: son historia inmutable y no se tocan.)

- [ ] **Step 9: Correr toda la suite**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: PASS, sin regresiones.

- [ ] **Step 10: Commit**

```bash
git add src/Valentinos.Domain/Entities/Site.cs \
        src/Valentinos.Infrastructure/Persistence/AppDbContext.cs \
        src/Valentinos.Infrastructure/Persistence/Migrations \
        src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs \
        src/Valentinos.Api/Controllers/AdminController.cs
git commit -m "refactor: Site.CcEmails -> Site.Emails (ya no es CC) y label 'Emails' en el panel"
```

---

### Task 3: Correo por site en `ReportEmailer`

**Files:**
- Modify: `src/Valentinos.Api/Reports/ReportEmailer.cs` (reescritura completa del archivo)
- Test: `tests/Valentinos.Tests/Reports/ReportEmailerTests.cs`

**Interfaces:**
- Consumes: `Site.Emails` (Task 2).
- Produces:
  - `ReportEmailer.SendSiteAsync(Tenant tenant, Site site, string period, DateTime? asOf = null, CancellationToken ct = default) -> Task<IReadOnlyList<string>>`
  - `ReportEmailer.SendAsync(Tenant tenant, string period, IReadOnlyList<string>? toOverride = null, bool includeCc = true, DateTime? asOf = null, CancellationToken ct = default) -> Task<IReadOnlyList<string>>` (firma **sin cambios**)
  - `ReportEmailer.SiteRecipients(Site site) -> string[]` (estático)
  Los tres los consume la Task 4.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Valentinos.Tests/Reports/ReportEmailerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Valentinos.Api.Reports;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Reports;

// Ningún test de este archivo abre una conexión SMTP: el IEmailSender es un doble
// que solo registra en memoria lo que se le pidió enviar.
public class ReportEmailerTests
{
    private sealed record Sent(IReadOnlyList<string> To, string Subject, string Body, bool IncludeCc);

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<Sent> Sends { get; } = new();
        public Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
            EmailAttachment? attachment = null, bool includeConfiguredCc = true, CancellationToken ct = default)
        {
            Sends.Add(new Sent(to.ToList(), subject, body, includeConfiguredCc));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    // Escenario común: tenant con dos sites, cada uno con un check-in de HOY hecho
    // por un empleado de nombre distinto (para poder detectar fugas de data entre sites).
    private static (AppDbContext db, Tenant tenant, Site s069, Site s127) NewScenario(
        bool conActividadEn127 = true)
    {
        var ctx = new FakeTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, ctx);

        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.NotificationEmails = "christopher.davey@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        ctx.Set(tenant.Id);
        var s069 = new Site { Code = "069", Slug = "XjUS3", Emails = "ramces.rodriguez@mastercorp.com" };
        var s127 = new Site
        {
            Code = "127 HCC",
            Slug = "Kp7Qm",
            Emails = "learsy.betancourt@mastercorp.com, carlos.reyes@mastercorp.com, gilberto.espinoza@mastercorp.com"
        };
        db.Sites.AddRange(s069, s127);
        db.SaveChanges();

        // Hora fija de hoy: el periodo "daily" filtra por fecha, no por hora.
        var hoy = DateTime.Today.AddHours(9);
        db.StatusCheckins.Add(new StatusCheckin
        {
            SiteId = s069.Id, EmployeeName = "EmpleadoDel069", AssetCodigo = "VAC-001",
            EstadoKey = "operational", CreatedAt = hoy
        });
        if (conActividadEn127)
            db.StatusCheckins.Add(new StatusCheckin
            {
                SiteId = s127.Id, EmployeeName = "EmpleadoDel127", AssetCodigo = "VAC-TIMESQUARE",
                EstadoKey = "operational", CreatedAt = hoy
            });
        db.SaveChanges();

        return (db, tenant, s069, s127);
    }

    private static ReportEmailer NewEmailer(AppDbContext db, IEmailSender sender)
        => new(db, sender, NullLogger<ReportEmailer>.Instance);

    [Fact]
    public async Task SendSiteAsync_EnviaSoloALosEmailsDelSite_YSinCc()
    {
        var (db, tenant, _, s127) = NewScenario();
        var sender = new RecordingEmailSender();

        var to = await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        var sent = Assert.Single(sender.Sends);
        Assert.Equal(new[]
        {
            "learsy.betancourt@mastercorp.com",
            "carlos.reyes@mastercorp.com",
            "gilberto.espinoza@mastercorp.com"
        }, sent.To);
        Assert.False(sent.IncludeCc);
        Assert.Contains("Site 127 HCC", sent.Subject);
        Assert.Equal(sent.To, to);
    }

    [Fact]
    public async Task SendSiteAsync_NoIncluyeDataDeOtroSite()
    {
        var (db, tenant, _, s127) = NewScenario();
        var sender = new RecordingEmailSender();

        await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        var sent = Assert.Single(sender.Sends);
        Assert.Contains("EmpleadoDel127", sent.Body);
        Assert.DoesNotContain("EmpleadoDel069", sent.Body);
    }

    [Fact]
    public async Task SendSiteAsync_SinActividadEnElPeriodo_NoEnvia()
    {
        var (db, tenant, _, s127) = NewScenario(conActividadEn127: false);
        var sender = new RecordingEmailSender();

        var to = await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        Assert.Empty(sender.Sends);
        Assert.Empty(to);
    }

    [Fact]
    public async Task SendSiteAsync_SiteSinEmails_NoEnvia()
    {
        var (db, tenant, _, s127) = NewScenario();
        s127.Emails = null;
        var sender = new RecordingEmailSender();

        var to = await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        Assert.Empty(sender.Sends);
        Assert.Empty(to);
    }

    [Fact]
    public async Task SendAsync_Consolidado_VaAlTenantConCc_YTraeAmbosSites()
    {
        var (db, tenant, _, _) = NewScenario();
        var sender = new RecordingEmailSender();

        await NewEmailer(db, sender).SendAsync(tenant, "daily");

        var sent = Assert.Single(sender.Sends);
        Assert.Equal(new[] { "christopher.davey@mastercorp.com" }, sent.To);
        Assert.True(sent.IncludeCc);
        Assert.Contains("EmpleadoDel069", sent.Body);
        Assert.Contains("EmpleadoDel127", sent.Body);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter ReportEmailerTests`
Expected: FAIL de compilación — `'ReportEmailer' no contiene una definición para 'SendSiteAsync'`.

- [ ] **Step 3: Reescribir `ReportEmailer`**

Reemplazar el contenido completo de `src/Valentinos.Api/Reports/ReportEmailer.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Api.Kpi;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Notifications;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Reports;

// Genera y envía por correo el reporte de KPIs (con el PDF adjunto). Dos salidas:
//  · SendSiteAsync   -> un site, a los destinatarios de ese site, SIN CC.
//  · SendAsync       -> todos los sites del tenant, a los destinatarios del tenant, CON CC.
// Reutilizado por el endpoint manual (/reports/{slug}/generate) y por el envío diario.
public class ReportEmailer
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<ReportEmailer> _logger;

    public ReportEmailer(AppDbContext db, IEmailSender email, ILogger<ReportEmailer> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    // Destinatarios del reporte de un site (TO directo, no CC).
    public static string[] SiteRecipients(Site site)
        => (site.Emails ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Reporte de UN site: solo su data, solo a sus destinatarios, sin el CC configurado.
    public async Task<IReadOnlyList<string>> SendSiteAsync(Tenant tenant, Site site, string period,
        DateTime? asOf = null, CancellationToken ct = default)
    {
        var to = SiteRecipients(site);
        if (to.Length == 0) return Array.Empty<string>();

        var now = asOf ?? DateTime.Now;
        var model = await BuildSiteKpiAsync(tenant, site.Id, site.Code, period, now, ct);

        return await SendModelsAsync(tenant, to, new[] { model },
            subjectSuffix: $" · Site {site.Code}", includeCc: false, now: now, ct: ct);
    }

    // Reporte CONSOLIDADO del tenant: todos sus sites, cada uno en su página del PDF.
    // `toOverride` fuerza los destinatarios (para pruebas); si es null, usa los del
    // tenant (NotificationEmails). `includeCc=false` omite el CC configurado. `asOf`
    // fija el "ahora" del cálculo (para simular el reporte de un día concreto).
    public async Task<IReadOnlyList<string>> SendAsync(Tenant tenant, string period,
        IReadOnlyList<string>? toOverride = null, bool includeCc = true,
        DateTime? asOf = null, CancellationToken ct = default)
    {
        var to = toOverride ?? EmailChannel.Recipients(tenant);
        if (to.Count == 0) return Array.Empty<string>();

        var now = asOf ?? DateTime.Now;

        var sites = await _db.Sites.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id)
            .OrderByDescending(s => s.Code)   // 127 HCC antes que 069
            .Select(s => new { s.Id, s.Code })
            .ToListAsync(ct);

        var models = new List<PeriodKpi>();
        foreach (var site in sites)
            models.Add(await BuildSiteKpiAsync(tenant, site.Id, site.Code, period, now, ct));

        return await SendModelsAsync(tenant, to, models,
            subjectSuffix: "", includeCc: includeCc, now: now, ct: ct);
    }

    // Arma el modelo KPI de un site: sus check-ins y sus "no disponibles" del periodo,
    // filtrados por SiteId. La ventana de 30 días cubre el periodo más largo (monthly);
    // Kpi.Compute recorta a la ventana real del periodo pedido.
    private async Task<PeriodKpi> BuildSiteKpiAsync(Tenant tenant, Guid siteId, string siteCode,
        string period, DateTime now, CancellationToken ct)
    {
        var since = now.Date.AddDays(-29);

        var checkins = await _db.StatusCheckins.IgnoreQueryFilters()
            .Where(c => c.SiteId == siteId && c.CreatedAt >= since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new KpiCheckin(c.EmployeeName, c.AssetCodigo, c.EstadoKey, c.Nota, c.CreatedAt))
            .ToListAsync(ct);

        var unav = await _db.UnavailableReports.IgnoreQueryFilters()
            .Where(u => u.SiteId == siteId && u.CreatedAt >= since)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new KpiUnavailable(u.EmployeeName, u.Nota, u.CreatedAt))
            .ToListAsync(ct);

        return Kpi.Kpi.Compute($"{tenant.Nombre} · Site {siteCode}", checkins, unav, now, period);
    }

    // Render + envío común a ambas salidas. Sin actividad en NINGÚN modelo no se envía
    // nada (coherente con el modal "sin data" del export a PDF).
    private async Task<IReadOnlyList<string>> SendModelsAsync(Tenant tenant, IReadOnlyList<string> to,
        IReadOnlyList<PeriodKpi> models, string subjectSuffix, bool includeCc, DateTime now, CancellationToken ct)
    {
        var totalActivity = models.Sum(m => m.Total + m.Un);
        if (models.Count == 0 || totalActivity == 0)
        {
            _logger.LogInformation("Sin actividad en el periodo para {Tenant}{Suffix}: no se envía reporte.",
                tenant.Nombre, subjectSuffix);
            return Array.Empty<string>();
        }

        var pName = models[0].Period switch { "weekly" => "Weekly", "monthly" => "Monthly", _ => "Daily" };
        var pdf = KpiPdf.RenderMulti(models);
        var subject = $"[ValentiSoft] {pName} report · {tenant.Nombre}{subjectSuffix}";
        var body = KpiHtml.RenderReportEmail(tenant.Nombre, models);
        var att = new EmailAttachment(pdf, $"KPI-{pName}-{now:yyyy-MM-dd}.pdf", "application/pdf");

        await _email.SendAsync(to, subject, body, isHtml: true, attachment: att,
            includeConfiguredCc: includeCc, ct: ct);
        _logger.LogInformation("📄 Reporte {P} ({Sites} sites) enviado a {To} (PDF {Bytes} bytes)",
            pName, models.Count, string.Join(", ", to), pdf.Length);
        return to;
    }
}
```

Diferencia de comportamiento a tener presente: el consolidado ahora arma el modelo de **todos** los sites (antes también lo hacía — el `foreach` original ya agregaba todos a `models`), así que el PDF sigue igual. Lo que cambia es solo la organización interna.

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter ReportEmailerTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Correr toda la suite**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: PASS, sin regresiones.

- [ ] **Step 6: Commit**

```bash
git add src/Valentinos.Api/Reports/ReportEmailer.cs \
        tests/Valentinos.Tests/Reports/ReportEmailerTests.cs
git commit -m "feat: reporte diario por site (solo su data, a sus destinatarios, sin CC)"
```

---

### Task 4: Orquestar el envío diario (`DailyReportRunner`)

**Files:**
- Create: `src/Valentinos.Api/Reports/DailyReportRunner.cs`
- Modify: `src/Valentinos.Api/Reports/DailyReportScheduler.cs:38-67`
- Modify: `src/Valentinos.Api/Program.cs:27-29`
- Test: `tests/Valentinos.Tests/Reports/DailyReportRunnerTests.cs`

**Interfaces:**
- Consumes: `ReportEmailer.SendSiteAsync`, `ReportEmailer.SendAsync`, `Site.Emails`.
- Produces: `DailyReportRunner.RunAsync(string period = "daily", DateTime? asOf = null, CancellationToken ct = default) -> Task`, registrado como servicio scoped.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Valentinos.Tests/Reports/DailyReportRunnerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Valentinos.Api.Reports;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Reports;

// Ningún test de este archivo abre una conexión SMTP.
public class DailyReportRunnerTests
{
    private sealed record Sent(IReadOnlyList<string> To, string Subject, bool IncludeCc);

    private sealed class RecordingEmailSender : IEmailSender
    {
        // Si el primer destinatario contiene este texto, el envío explota (para
        // probar que un site que falla no tumba a los demás ni al consolidado).
        public string? FallaSiContiene { get; set; }
        public List<Sent> Sends { get; } = new();

        public Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
            EmailAttachment? attachment = null, bool includeConfiguredCc = true, CancellationToken ct = default)
        {
            if (FallaSiContiene is not null && to.Any(t => t.Contains(FallaSiContiene)))
                throw new InvalidOperationException("SMTP caído");
            Sends.Add(new Sent(to.ToList(), subject, includeConfiguredCc));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    // Tenant con: un site con destinatarios y actividad, y otro SIN destinatarios.
    private static AppDbContext NewScenario()
    {
        var ctx = new FakeTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, ctx);

        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.NotificationEmails = "christopher.davey@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        ctx.Set(tenant.Id);
        var s069 = new Site { Code = "069", Slug = "XjUS3", Emails = "ramces.rodriguez@mastercorp.com" };
        var sinEmails = new Site { Code = "003", Slug = "Ra9Zt", Emails = null };
        db.Sites.AddRange(s069, sinEmails);
        db.SaveChanges();

        var hoy = DateTime.Today.AddHours(9);
        db.StatusCheckins.AddRange(
            new StatusCheckin { SiteId = s069.Id, EmployeeName = "Ana", AssetCodigo = "VAC-001", EstadoKey = "operational", CreatedAt = hoy },
            new StatusCheckin { SiteId = sinEmails.Id, EmployeeName = "Beto", AssetCodigo = "VAC-001", EstadoKey = "operational", CreatedAt = hoy });
        db.SaveChanges();

        return db;
    }

    private static DailyReportRunner NewRunner(AppDbContext db, IEmailSender sender)
        => new(db, new ReportEmailer(db, sender, NullLogger<ReportEmailer>.Instance),
               NullLogger<DailyReportRunner>.Instance);

    [Fact]
    public async Task RunAsync_EnviaUnCorreoPorSiteConEmails_MasElConsolidado()
    {
        var db = NewScenario();
        var sender = new RecordingEmailSender();

        await NewRunner(db, sender).RunAsync();

        Assert.Equal(2, sender.Sends.Count);

        var porSite = sender.Sends.Single(s => s.To.Contains("ramces.rodriguez@mastercorp.com"));
        Assert.False(porSite.IncludeCc);
        Assert.Contains("Site 069", porSite.Subject);

        var consolidado = sender.Sends.Single(s => s.To.Contains("christopher.davey@mastercorp.com"));
        Assert.True(consolidado.IncludeCc);
        Assert.DoesNotContain("Site", consolidado.Subject);
    }

    [Fact]
    public async Task RunAsync_SiFallaElCorreoDeUnSite_IgualEnviaElConsolidado()
    {
        var db = NewScenario();
        var sender = new RecordingEmailSender { FallaSiContiene = "ramces" };

        await NewRunner(db, sender).RunAsync();

        var consolidado = Assert.Single(sender.Sends);
        Assert.Contains("christopher.davey@mastercorp.com", consolidado.To);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter DailyReportRunnerTests`
Expected: FAIL de compilación — el tipo `DailyReportRunner` no existe.

- [ ] **Step 3: Crear el runner**

Crear `src/Valentinos.Api/Reports/DailyReportRunner.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Reports;

// Orquesta el envío diario de reportes: por cada tenant, un correo por cada site
// con destinatarios configurados (solo su data, sin CC), y luego el consolidado de
// todos los sites al tenant (con CC). Vive aparte del BackgroundService para poder
// probarlo sin temporizadores.
public class DailyReportRunner
{
    private readonly AppDbContext _db;
    private readonly ReportEmailer _emailer;
    private readonly ILogger<DailyReportRunner> _logger;

    public DailyReportRunner(AppDbContext db, ReportEmailer emailer, ILogger<DailyReportRunner> logger)
    {
        _db = db;
        _emailer = emailer;
        _logger = logger;
    }

    public async Task RunAsync(string period = "daily", DateTime? asOf = null, CancellationToken ct = default)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters().ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            var sites = await _db.Sites.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenant.Id && s.Emails != null && s.Emails != "")
                .OrderByDescending(s => s.Code)
                .ToListAsync(ct);

            // Un correo por site. Cada envío aislado: que falle uno no debe impedir
            // los demás ni el consolidado.
            foreach (var site in sites)
            {
                try
                {
                    var sent = await _emailer.SendSiteAsync(tenant, site, period, asOf, ct);
                    if (sent.Count > 0)
                        _logger.LogInformation("📍 Reporte del site {Site} enviado a {To}",
                            site.Code, string.Join(", ", sent));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fallo al enviar el reporte del site {Site} ({Tenant})",
                        site.Code, tenant.Nombre);
                }
            }

            if (string.IsNullOrWhiteSpace(tenant.NotificationEmails)) continue;

            try
            {
                var sent = await _emailer.SendAsync(tenant, period, asOf: asOf, ct: ct);
                if (sent.Count > 0)
                    _logger.LogInformation("📅 Reporte consolidado ({Tenant}) enviado a {To}",
                        tenant.Nombre, string.Join(", ", sent));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo al enviar el reporte consolidado de {Tenant}", tenant.Nombre);
            }
        }
    }
}
```

- [ ] **Step 4: Simplificar el scheduler para que delegue en el runner**

En `src/Valentinos.Api/Reports/DailyReportScheduler.cs`, reemplazar el método `SendDailyForAllTenantsAsync` completo (líneas 38-67) por:

```csharp
    private async Task SendDailyForAllTenantsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<DailyReportRunner>();
            await runner.RunAsync(ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo general en el scheduler de reporte diario");
        }
    }
```

Quitar los `using` que quedan sin uso al tope del archivo (`Microsoft.EntityFrameworkCore` y `Valentinos.Infrastructure.Persistence`), y actualizar el comentario de cabecera de la clase a:

```csharp
// Dispara el envío DIARIO de reportes todos los días a las 10:00 (hora local del
// servidor), delegando en DailyReportRunner. Sin dependencias externas.
// NOTA: requiere que la app esté corriendo a esa hora (idealmente instalada como servicio).
```

- [ ] **Step 5: Registrar el runner en el contenedor**

En `src/Valentinos.Api/Program.cs`, reemplazar el bloque de registro de reportes:

```csharp
// Servicio de envío de reportes + scheduler diario (10:00 hora local).
builder.Services.AddScoped<Valentinos.Api.Reports.ReportEmailer>();
builder.Services.AddScoped<Valentinos.Api.Reports.DailyReportRunner>();
builder.Services.AddHostedService<Valentinos.Api.Reports.DailyReportScheduler>();
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter DailyReportRunnerTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Correr toda la suite**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: PASS, sin regresiones.

- [ ] **Step 8: Commit**

```bash
git add src/Valentinos.Api/Reports/DailyReportRunner.cs \
        src/Valentinos.Api/Reports/DailyReportScheduler.cs \
        src/Valentinos.Api/Program.cs \
        tests/Valentinos.Tests/Reports/DailyReportRunnerTests.cs
git commit -m "feat: envío diario 10:00 manda un correo por site y luego el consolidado"
```

---

### Task 5: Sembrar el site 127 HCC

**Files:**
- Modify: `src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs`
- Modify: `src/Valentinos.Api/Program.cs:141-142`
- Test: `tests/Valentinos.Tests/Persistence/DemoSeederTests.cs`

**Interfaces:**
- Consumes: `Employee.SiteId` (Task 1), `Site.Emails` (Task 2).
- Produces: nada que consuman otras tareas.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Valentinos.Tests/Persistence/DemoSeederTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Persistence;

public class DemoSeederTests
{
    private sealed class SeederTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    private const string Emails127 =
        "learsy.betancourt@mastercorp.com, carlos.reyes@mastercorp.com, gilberto.espinoza@mastercorp.com";

    private static (AppDbContext db, SeederTenantContext ctx) NewDbConTenant()
    {
        var ctx = new SeederTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, ctx);
        db.Tenants.Add(Tenant.Create("mastercorp", "MasterCorp"));
        db.SaveChanges();
        return (db, ctx);
    }

    [Fact]
    public async Task Seed_CreaElSite127ConSus13VacuumsYSus5Empleados()
    {
        var (db, ctx) = NewDbConTenant();

        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        var site = await db.Sites.IgnoreQueryFilters().FirstAsync(s => s.Slug == "Kp7Qm");
        Assert.Equal("127 HCC", site.Code);
        Assert.Equal(Emails127, site.Emails);

        var vacs = await db.Assets.IgnoreQueryFilters()
            .Where(a => a.SiteId == site.Id).Select(a => a.Codigo).ToListAsync();
        Assert.Equal(13, vacs.Count);
        Assert.Contains("VAC-001", vacs);
        Assert.Contains("VAC-011", vacs);
        Assert.DoesNotContain("VAC-012", vacs);
        Assert.Contains("VAC-TIMESQUARE", vacs);
        Assert.Contains("VAC-FRONTDESK", vacs);

        var emps = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.SiteId == site.Id).Select(e => e.Nombre).ToListAsync();
        Assert.Equal(5, emps.Count);
        Assert.Contains("Gonzalez Guerra, Yanet", emps);
        Assert.Contains("Jeronimo, Brissman", emps);
        Assert.Contains("Martea, Lidia", emps);
        Assert.Contains("Pacheco, Wendy", emps);
        Assert.Contains("Zdor, Galina", emps);
    }

    [Fact]
    public async Task Seed_ElSite069ConservaSus16VacuumsYSus16Empleados()
    {
        var (db, ctx) = NewDbConTenant();

        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        var site = await db.Sites.IgnoreQueryFilters().FirstAsync(s => s.Slug == "XjUS3");
        Assert.Equal("069", site.Code);
        Assert.Equal(16, await db.Assets.IgnoreQueryFilters().CountAsync(a => a.SiteId == site.Id));
        Assert.Equal(16, await db.Employees.IgnoreQueryFilters().CountAsync(e => e.SiteId == site.Id));
    }

    [Fact]
    public async Task Seed_EsIdempotente_NoDuplicaNada()
    {
        var (db, ctx) = NewDbConTenant();

        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");
        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        Assert.Equal(4, await db.Sites.IgnoreQueryFilters().CountAsync());
        Assert.Equal(29, await db.Assets.IgnoreQueryFilters().CountAsync());   // 16 del 069 + 13 del 127
        Assert.Equal(21, await db.Employees.IgnoreQueryFilters().CountAsync()); // 16 + 5
    }

    [Fact]
    public async Task Seed_RenombraElPlaceholder002ExistenteA127HCC()
    {
        var (db, ctx) = NewDbConTenant();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync();
        ctx.Set(tenant.Id);
        db.Sites.Add(new Site { Code = "002", Slug = "Kp7Qm", Emails = null });
        db.SaveChanges();

        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        var site = await db.Sites.IgnoreQueryFilters().FirstAsync(s => s.Slug == "Kp7Qm");
        Assert.Equal("127 HCC", site.Code);
        Assert.Equal(Emails127, site.Emails);
    }

    [Fact]
    public async Task Seed_NoPisaLosEmailsEditadosDesdeElPanel()
    {
        var (db, ctx) = NewDbConTenant();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync();
        ctx.Set(tenant.Id);
        db.Sites.Add(new Site { Code = "002", Slug = "Kp7Qm", Emails = "otro@mastercorp.com" });
        db.SaveChanges();

        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        var site = await db.Sites.IgnoreQueryFilters().FirstAsync(s => s.Slug == "Kp7Qm");
        Assert.Equal("127 HCC", site.Code);
        Assert.Equal("otro@mastercorp.com", site.Emails);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter DemoSeederTests`
Expected: FAIL — el site `Kp7Qm` sigue teniendo `Code = "002"` y no tiene vacuums ni empleados.

- [ ] **Step 3: Reemplazar el bloque de sites del seeder**

En `src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs`, reemplazar todo el bloque que va desde el comentario `// ---- Sites: ...` hasta la línea `var site069 = await db.Sites.FirstAsync(x => x.Code == "069");` por:

```csharp
        // ---- Sites del tenant. La identidad estable es el SLUG (el Code puede
        // cambiar: el placeholder "002" se convirtió en el site real "127 HCC"). ----
        var seedSites = new[]
        {
            (Code: "069", Slug: "XjUS3", Emails: (string?)"ramces.rodriguez@mastercorp.com"),
            (Code: "127 HCC", Slug: "Kp7Qm", Emails: (string?)"learsy.betancourt@mastercorp.com, carlos.reyes@mastercorp.com, gilberto.espinoza@mastercorp.com"),
            (Code: "003", Slug: "Ra9Zt", Emails: (string?)null),
            (Code: "004", Slug: "Bn4Wc", Emails: (string?)null),
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
            if (existing.Code == "002" && s.Code == "127 HCC") existing.Code = s.Code;
            // Los emails solo se siembran si están vacíos: no pisar lo editado en el panel.
            if (string.IsNullOrWhiteSpace(existing.Emails) && !string.IsNullOrWhiteSpace(s.Emails))
                existing.Emails = s.Emails;
        }
        await db.SaveChangesAsync();

        var site069 = await db.Sites.FirstAsync(x => x.Slug == "XjUS3");
        var site127 = await db.Sites.FirstAsync(x => x.Slug == "Kp7Qm");
```

- [ ] **Step 4: Reemplazar el bloque de vacuums del seeder**

Reemplazar el bloque que va desde el comentario `// Sembrar hasta 16 vacuums (VAC-001..VAC-016) en el Site 069.` hasta el `await db.SaveChangesAsync();` que le sigue (el `for` con `nAssets` y `tipo.CorrelativoActual`) por:

```csharp
        // Vacuums por site. La numeración es POR SITE desde la migración
        // SiteScopedVacuumCode, así que NO se usa el contador global del AssetType
        // (que es compartido entre sites) ni un conteo global de assets.
        await SeedVacuumsAsync(db, tipo.Id, site069.Id, Vacuums069);
        await SeedVacuumsAsync(db, tipo.Id, site127.Id, Vacuums127);
```

- [ ] **Step 5: Reemplazar el bloque de empleados del seeder**

Reemplazar el bloque:

```csharp
        // Empleados de MasterCorp (para el autocompletar del formulario).
        if (!await db.Employees.AnyAsync())
        {
            foreach (var nombre in Empleados)
                db.Employees.Add(new Employee { Nombre = nombre });
            await db.SaveChangesAsync();
        }
```

por:

```csharp
        // Empleados POR SITE (para el autocompletar del formulario de cada site).
        await SeedEmployeesAsync(db, site069.Id, Empleados069);
        await SeedEmployeesAsync(db, site127.Id, Empleados127);
```

- [ ] **Step 6: Agregar los helpers y las listas de datos**

Al final de la clase `DemoSeeder`, **antes** del array `Empleados`, agregar los dos helpers:

```csharp
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

    private static readonly string[] Empleados127 =
    {
        "Gonzalez Guerra, Yanet",
        "Jeronimo, Brissman",
        "Martea, Lidia",
        "Pacheco, Wendy",
        "Zdor, Galina"
    };
```

Después renombrar el array `Empleados` existente a `Empleados069`:

```csharp
    private static readonly string[] Empleados069 =
```

y actualizar sus **tres** usos restantes, todos dentro de los bloques `if (seedFakeReports ...)`:
`Empleados[rnd.Next(Empleados.Length)]` → `Empleados069[rnd.Next(Empleados069.Length)]`, y
`Empleados[rnd2.Next(Empleados.Length)]` → `Empleados069[rnd2.Next(Empleados069.Length)]`.

Verificar que el archivo tenga `using System.Linq;` disponible (viene por `ImplicitUsings`, no hace falta agregarlo).

- [ ] **Step 7: Cambiar el destinatario del consolidado**

En `src/Valentinos.Api/Program.cs`, en la llamada a `DemoSeeder.SeedAsync`, reemplazar el string de destinatarios:

```csharp
        await DemoSeeder.SeedAsync(db, tenantContext,
            "christopher.davey@mastercorp.com");
```

- [ ] **Step 8: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter DemoSeederTests`
Expected: PASS (5 tests).

Si `Seed_EsIdempotente_NoDuplicaNada` falla por el conteo de assets, revisar que el backfill de huérfanos (`a.SiteId == Guid.Empty`) no esté creando filas: solo debe reasignar.

- [ ] **Step 9: Correr toda la suite**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: PASS, sin regresiones.

- [ ] **Step 10: Commit**

```bash
git add src/Valentinos.Infrastructure/Persistence/DemoSeeder.cs \
        src/Valentinos.Api/Program.cs \
        tests/Valentinos.Tests/Persistence/DemoSeederTests.cs
git commit -m "feat: site 127 HCC con 13 vacuums, 5 empleados y sus 3 destinatarios"
```

---

### Task 6: Verificación visual (sin enviar correos)

**Files:** ninguno (solo revisión).

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: nada.

- [ ] **Step 1: Apagar SMTP para la sesión de verificación**

El `DailyReportScheduler` está registrado siempre y dispara a las 10:00 con los user-secrets cargados. Para revisar la app sin riesgo, arrancarla con SMTP forzado a apagado por variable de entorno (gana sobre los user-secrets):

```bash
Smtp__Enabled=false dotnet run --project src/Valentinos.Api/Valentinos.Api.csproj
```

En PowerShell:

```powershell
$env:Smtp__Enabled = "false"; dotnet run --project src/Valentinos.Api/Valentinos.Api.csproj
```

Confirmar en el log de arranque que aparece `⏰ Próximo reporte diario programado para ...` — y que `Smtp:Enabled` está en false, de modo que aunque llegara la hora `SmtpEmailSender` retorna sin conectar.

- [ ] **Step 2: Verificar que las migraciones se aplicaron**

Al arrancar, la app corre `db.Database.MigrateAsync()`. Confirmar en el log que no hay excepciones y que aparecen `EmployeeSiteScoped` y `SiteEmailsRename` como aplicadas.

- [ ] **Step 3: Revisar el panel de sites**

Abrir `http://localhost:<puerto>/admin/sites` (login con un admin del tenant).
Verificar:
- La columna se llama **Emails** (no "Recipients (CC)").
- Aparece la fila `127 HCC` con **13** vacuums y los 3 correos.
- Aparece la fila `069` con **16** vacuums y `ramces.rodriguez@mastercorp.com`.
- Siguen `003` y `004` vacíos.

- [ ] **Step 4: Revisar la configuración del site 127**

Abrir `/admin/sites/Kp7Qm`.
Verificar:
- El label del campo dice **Emails — comma separated**.
- Los chips muestran los 13 códigos, incluidos `VAC-TIMESQUARE` y `VAC-FRONTDESK`.

- [ ] **Step 5: Revisar el QR sheet y el dashboard del site**

Abrir `/admin/sites/Kp7Qm/qr` y `/reports?site=Kp7Qm`.
Verificar que el QR sheet lista los 13 vacuums y que el dashboard carga sin errores (sin data aún, es lo esperado).

- [ ] **Step 6: Revisar el autocompletar de empleados**

Abrir `http://localhost:<puerto>/api/public/Kp7Qm/employees`.
Expected: exactamente los 5 nombres del 127 HCC, y **ninguno** de los 16 del 069.

Abrir `http://localhost:<puerto>/api/public/XjUS3/employees`.
Expected: los 16 nombres del 069, y ninguno del 127.

- [ ] **Step 7: Detener la app**

Cortar el proceso. **No** dejarla corriendo: con los user-secrets normales volvería a tener SMTP activo.

- [ ] **Step 8: Confirmar con el usuario antes de cualquier envío**

Reportar el estado y **preguntar explícitamente** antes de habilitar SMTP o disparar cualquier envío de prueba. Hasta esa autorización, no se envía ningún correo.

---

## Notas de riesgo

- **La migración de renombre (Task 2, Step 7) es el punto delicado del plan.** Si EF genera `DropColumn` + `AddColumn` en vez de `RenameColumn`, se pierden los emails ya configurados en la base de producción. El paso incluye la verificación y la corrección manual.
- **El asunto del correo por site depende del `Code` del site.** Si alguien edita el code desde el panel, el asunto cambia. Es el comportamiento deseado.
- **`Smtp:Cc` se aplica a todos los correos que no pasen `includeConfiguredCc: false`** — incluidos los de autenticación. Eso ya era así antes de este trabajo y no se toca.
