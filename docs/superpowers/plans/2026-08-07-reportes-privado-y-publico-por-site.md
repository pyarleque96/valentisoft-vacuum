# Reportes: vista privada multi-site y vista pública por site — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar la vista de KPIs multi-site detrás de login y publicar en su lugar una vista acotada a un solo site, que no permita alcanzar ni enumerar los demás.

**Architecture:** Tres cambios sobre una app ASP.NET Core con Clean Architecture. Primero `KpiHtml.Render` aprende a omitir el selector de sites y a apuntar a las rutas públicas cuando no recibe lista de sites; luego se agregan las dos rutas públicas por slug; por último se cierran con autenticación las dos rutas multi-site existentes. El cálculo de KPIs y el PDF no se tocan.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core 10 (InMemory en tests), xUnit, `WebApplicationFactory<Program>` para tests de endpoint.

## Global Constraints

- **No se envía ningún correo, ni de prueba.** No arrancar la app (`dotnet run`); los user-secrets tienen `Smtp:Enabled=true`. No invocar `POST /admin/reports/sample`.
- **No se toca la base de datos real.** No correr migraciones ni conectarse a `(localdb)\MSSQLLocalDB`.
- **Los commits NO llevan trailers de Claude** — ni `Co-Authored-By: Claude` ni `Claude-Session:`. Solo autoría de pyarleque96.
- Comentarios de código en español (convención del repo); textos visibles al usuario en inglés.
- Rutas privadas: `GET /reports` y `GET /reports/pdf`, con `[Authorize(Roles = "admin")]`.
- Rutas públicas: `GET /{siteSlug}/reports` y `GET /{siteSlug}/reports/pdf`, con `[AllowAnonymous]`.
- La vista pública **ignora cualquier `?site=`**: el site sale del segmento de la URL y de ningún otro lado.
- El HTML público **no debe contener** el `<select id="site">`, ni el slug, ni el código de ningún otro site. Ocultarlo por CSS no cuenta.
- Comando de tests: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
- **Fallo preexistente, ajeno a este trabajo:** `PublicReportsEndpointTests.PostReport_ActivoExistente_Crea201` ya falla en `master`. La puerta de aceptación es "sin fallos NUEVOS además de ese", no "suite verde".

## File Structure

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `src/Valentinos.Api/Kpi/KpiHtml.cs` | omite el selector y ajusta las URLs cuando `sites` viene vacía | 1 |
| `tests/Valentinos.Tests/Reports/KpiHtmlRenderTests.cs` (nuevo) | prueba pura del HTML renderizado | 1 |
| `src/Valentinos.Api/Controllers/DemoController.cs` | rutas públicas por slug + cierre de las privadas | 2, 3 |
| `tests/Valentinos.Tests/Api/SiteReportsEndpointTests.cs` (nuevo) | rutas públicas: 200, 404 y aislamiento | 2 |
| `tests/Valentinos.Tests/Api/ReportsAuthEndpointTests.cs` (nuevo) | rutas privadas: redirect a login sin cookie | 3 |

Orden: la Task 2 consume el cambio de render de la Task 1; la Task 3 va última para no romper las pruebas de las anteriores mientras se escriben.

---

### Task 1: `KpiHtml.Render` omite el selector cuando no hay sites

**Files:**
- Modify: `src/Valentinos.Api/Kpi/KpiHtml.cs:26-31` y `:191-199`
- Test: `tests/Valentinos.Tests/Reports/KpiHtmlRenderTests.cs`

**Nota sobre el namespace del test:** va en `Valentinos.Tests.Reports`, no en `Valentinos.Tests.Kpi`. La clase bajo prueba vive en el namespace `Valentinos.Api.Kpi` y además se llama `Kpi`; un namespace de test terminado en `.Kpi` haría que el identificador `Kpi` se resuelva al namespace del test y no a la clase, rompiendo la compilación de forma confusa.

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `KpiHtml.Render(PeriodKpi m, string slug, IReadOnlyList<KpiSiteOption> sites)` — **firma sin cambios**. Con `sites` vacía omite el selector y apunta a `/{slug}/reports` y `/{slug}/reports/pdf`; con `sites` no vacía se comporta como hoy. La Task 2 lo llama con una lista vacía.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Valentinos.Tests/Kpi/KpiHtmlRenderTests.cs`:

```csharp
using Valentinos.Api.Kpi;
using Xunit;

namespace Valentinos.Tests.Reports;

// La vista pública de un site no debe exponer NADA de los demás sites: ni el selector,
// ni sus slugs, ni sus códigos. Estas pruebas son sobre el HTML renderizado, que es lo
// que realmente ve quien abre la página.
public class KpiHtmlRenderTests
{
    private static PeriodKpi Modelo(string siteCode = "127 HCC", string period = "daily")
        => Valentinos.Api.Kpi.Kpi.Compute($"MasterCorp · Site {siteCode}",
            System.Array.Empty<KpiCheckin>(), System.Array.Empty<KpiUnavailable>(),
            System.DateTime.Now, period);

    [Fact]
    public void Render_SinSites_NoEmiteElSelectorNiDatosDeOtrosSites()
    {
        var html = KpiHtml.Render(Modelo(), "Kp7Qm", System.Array.Empty<KpiSiteOption>());

        Assert.DoesNotContain("id=\"site\"", html);
        Assert.DoesNotContain("XjUS3", html);
        Assert.DoesNotContain("Site 069", html);
    }

    [Fact]
    public void Render_SinSites_ApuntaALasRutasPublicasDelSite()
    {
        var html = KpiHtml.Render(Modelo(), "Kp7Qm", System.Array.Empty<KpiSiteOption>());

        Assert.Contains("/Kp7Qm/reports/pdf", html);
        Assert.Contains("/Kp7Qm/reports?period=", html);
        Assert.DoesNotContain("/reports/pdf?site=", html);
        Assert.DoesNotContain("/reports?site=", html);
    }

    [Fact]
    public void Render_ConSites_EmiteElSelectorYLasRutasPrivadas()
    {
        var sites = new[]
        {
            new KpiSiteOption("Kp7Qm", "127 HCC"),
            new KpiSiteOption("XjUS3", "069"),
        };

        var html = KpiHtml.Render(Modelo(), "Kp7Qm", sites);

        Assert.Contains("id=\"site\"", html);
        Assert.Contains("XjUS3", html);
        Assert.Contains("/reports/pdf?site=Kp7Qm", html);
    }

    [Fact]
    public void Render_SinSites_ConservaPeriodoPdfYToggleDeIdioma()
    {
        var html = KpiHtml.Render(Modelo(), "Kp7Qm", System.Array.Empty<KpiSiteOption>());

        Assert.Contains("id=\"period\"", html);      // selector de periodo
        Assert.Contains("data-i18n=\"dlpdf\"", html); // botón de PDF
        Assert.Contains("setLang('es')", html);       // toggle ES/EN
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter KpiHtmlRenderTests`
Expected: FAIL — los dos primeros tests fallan porque hoy el selector se emite siempre y las URLs siempre cuelgan de `/reports`.

- [ ] **Step 3: Calcular el modo y las URLs en `Render`**

En `src/Valentinos.Api/Kpi/KpiHtml.cs`, reemplazar el bloque que construye `siteOpts` (el que empieza con el comentario `// Opciones del dropdown de sites; el actual queda seleccionado.`) por:

```csharp
        // Dos modos de la misma vista:
        //  · privada  -> `sites` trae los sites del tenant: se pinta el dropdown y las
        //                URLs cuelgan de /reports con ?site=.
        //  · pública  -> `sites` viene vacía: NO se emite el bloque del selector (el HTML
        //                no debe contener slugs ni códigos de otros sites) y las URLs
        //                cuelgan de /{slug}/reports.
        var multiSite = sites.Count > 0;

        var siteOpts = new StringBuilder();
        foreach (var s in sites)
            siteOpts.Append($@"<option value=""{H(s.Slug)}""{(s.Slug == slug ? " selected" : "")}>Site {H(s.Code)}</option>");

        var siteSelect = multiSite
            ? $@"<select id=""site"" onchange=""location.href='/reports?site='+this.value+'&period={H(m.Period)}'"">{siteOpts}</select>"
            : string.Empty;

        var periodHref = multiSite
            ? $"/reports?site={H(slug)}&period="
            : $"/{H(slug)}/reports?period=";

        var pdfHref = multiSite
            ? $"/reports/pdf?site={H(slug)}&period={H(m.Period)}"
            : $"/{H(slug)}/reports/pdf?period={H(m.Period)}";
```

- [ ] **Step 4: Usar las variables en el marcado**

En el mismo archivo, reemplazar el bloque `<div class="controls">` completo por:

```csharp
    <div class=""controls"">
      {siteSelect}
      <select id=""period"" onchange=""location.href='{periodHref}'+this.value"">
        {Sel("daily", "Daily")}{Sel("weekly", "Weekly")}{Sel("monthly", "Monthly")}
      </select>
      <a class=""btn"" href=""{pdfHref}"" target=""_blank"" data-i18n=""dlpdf"" onclick=""return pdfGuard(event)"">📄 Download PDF</a>
    </div>
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter KpiHtmlRenderTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: sin fallos nuevos más allá del preexistente conocido.

- [ ] **Step 7: Commit**

```bash
git add src/Valentinos.Api/Kpi/KpiHtml.cs tests/Valentinos.Tests/Kpi/KpiHtmlRenderTests.cs
git commit -m "feat(kpi): la vista de KPIs omite el selector de sites cuando se renderiza para un solo site"
```

---

### Task 2: Rutas públicas `/{siteSlug}/reports` y `/{siteSlug}/reports/pdf`

**Files:**
- Modify: `src/Valentinos.Api/Controllers/DemoController.cs` (extraer helper en `:412-429`, agregar dos acciones junto a las de reportes en `:439-495`)
- Test: `tests/Valentinos.Tests/Api/SiteReportsEndpointTests.cs`

**Interfaces:**
- Consumes: `KpiHtml.Render(...)` con lista vacía (Task 1).
- Produces: las dos rutas públicas. La Task 3 no depende de ellas.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Valentinos.Tests/Api/SiteReportsEndpointTests.cs`:

```csharp
using System.Net;
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

// La vista pública de KPIs de un site: accesible sin login, acotada a su propio site,
// y sin exponer los demás sites del tenant.
public class SiteReportsEndpointTests : IClassFixture<SiteReportsEndpointTests.Factory>
{
    private readonly Factory _factory;
    public SiteReportsEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task GetSiteReports_SinLogin_Devuelve200()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/SiteUno/reports");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetSiteReports_SlugInexistente_Devuelve404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/NoExiste/reports");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetSiteReportsPdf_SinLogin_DevuelvePdf()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/SiteUno/reports/pdf");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
    }

    // La prueba que distingue "quitamos el combo" de "la página no expone los otros sites".
    [Fact]
    public async Task GetSiteReports_NoExponeLosOtrosSitesDelTenant()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/SiteUno/reports");

        Assert.DoesNotContain("id=\"site\"", html);
        Assert.DoesNotContain("SiteDos", html);
        Assert.DoesNotContain("Site 002", html);
    }

    // Aislamiento de la DATA, no solo de la navegación.
    [Fact]
    public async Task GetSiteReports_NoMuestraCheckinsDeOtroSite()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/SiteUno/reports");

        Assert.Contains("EmpleadoDelUno", html);
        Assert.DoesNotContain("EmpleadoDelDos", html);
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
                    o.UseInMemoryDatabase("SiteReportsEndpointTests"));
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

            // Site y StatusCheckin son ITenantOwned: el write-path exige tenant en contexto.
            tenantCtx.Set(tenant.Id);
            var uno = new Site { Code = "001", Slug = "SiteUno" };
            var dos = new Site { Code = "002", Slug = "SiteDos" };
            db.Sites.AddRange(uno, dos);
            db.SaveChanges();

            // Hora fija de hoy: el periodo "daily" filtra por fecha, no por hora.
            var hoy = DateTime.Today.AddHours(9);
            db.StatusCheckins.AddRange(
                new StatusCheckin { SiteId = uno.Id, EmployeeName = "EmpleadoDelUno", AssetCodigo = "VAC-001", EstadoKey = "operational", CreatedAt = hoy },
                new StatusCheckin { SiteId = dos.Id, EmployeeName = "EmpleadoDelDos", AssetCodigo = "VAC-001", EstadoKey = "operational", CreatedAt = hoy });
            db.SaveChanges();

            return host;
        }
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter SiteReportsEndpointTests`
Expected: FAIL con 404 en todos los casos — las rutas todavía no existen.

- [ ] **Step 3: Extraer la carga de check-ins a un helper que recibe el site ya resuelto**

En `src/Valentinos.Api/Controllers/DemoController.cs`, reemplazar el método `LoadSiteCheckinsAsync` completo por estos dos:

```csharp
    // Check-ins y "no disponibles" de los últimos 30 días de un site YA resuelto.
    private async Task<(List<Kpi.KpiCheckin> checkins, List<Kpi.KpiUnavailable> unavailable)>
        LoadCheckinsForSiteAsync(Domain.Entities.Site site)
    {
        var since = DateTime.Now.Date.AddDays(-29);
        var list = await _db.StatusCheckins.IgnoreQueryFilters()
            .Where(c => c.SiteId == site.Id && c.CreatedAt >= since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new Kpi.KpiCheckin(c.EmployeeName, c.AssetCodigo, c.EstadoKey, c.Nota, c.CreatedAt))
            .ToListAsync();
        var unav = await _db.UnavailableReports.IgnoreQueryFilters()
            .Where(u => u.SiteId == site.Id && u.CreatedAt >= since)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new Kpi.KpiUnavailable(u.EmployeeName, u.Nota, u.CreatedAt))
            .ToListAsync();
        return (list, unav);
    }

    // Variante para la vista privada, que resuelve el site por "key" (guid o slug) dentro
    // del tenant del subdominio.
    private async Task<(Domain.Entities.Tenant? tenant, Domain.Entities.Site? site, List<Kpi.KpiCheckin> checkins, List<Kpi.KpiUnavailable> unavailable)> LoadSiteCheckinsAsync(string key)
    {
        var (_, site) = await ResolveSiteInTenantAsync(key);
        if (site is null) return (null, null, new(), new());
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == Tid);
        var (list, unav) = await LoadCheckinsForSiteAsync(site);
        return (tenant, site, list, unav);
    }
```

- [ ] **Step 4: Agregar las dos acciones públicas**

En el mismo archivo, justo **después** del método `KpiPdfPreview` (la acción de `/reports/pdf`), agregar:

```csharp
    // ---------- Vista PÚBLICA de KPIs, por site ----------
    // /{siteSlug}/reports: solo la data de ese site y SIN el dropdown de sites. El slug
    // aleatorio de la URL es la única barrera, igual que en las rutas de los QR impresos.
    // Se resuelve con ResolveSiteAsync (slug exacto) y NO con ReportsSiteKeyAsync, que
    // acepta guid y cae al site por defecto: aquí la URL manda y un ?site= se ignora.
    [AllowAnonymous]
    [HttpGet("/{siteSlug}/reports")]
    public async Task<IActionResult> SiteKpiPage(string siteSlug, [FromQuery] string? period)
    {
        var (tenant, site) = await ResolveSiteAsync(siteSlug);
        if (tenant is null || site is null) return NotFound();

        var (list, unav) = await LoadCheckinsForSiteAsync(site);
        var model = Kpi.Kpi.Compute($"{tenant.Nombre} · Site {site.Code}", list, unav, DateTime.Now, period ?? "daily");
        return Content(Kpi.KpiHtml.Render(model, site.Slug, System.Array.Empty<Kpi.KpiSiteOption>()),
            "text/html; charset=utf-8");
    }

    // PDF de la vista pública: el mismo reporte, del mismo site y de ninguno más.
    [AllowAnonymous]
    [HttpGet("/{siteSlug}/reports/pdf")]
    public async Task<IActionResult> SiteKpiPdf(string siteSlug, [FromQuery] string? period)
    {
        var (tenant, site) = await ResolveSiteAsync(siteSlug);
        if (tenant is null || site is null) return NotFound();

        var (list, unav) = await LoadCheckinsForSiteAsync(site);
        var model = Kpi.Kpi.Compute($"{tenant.Nombre} · Site {site.Code}", list, unav, DateTime.Now, period ?? "daily");
        return File(Kpi.KpiPdf.Render(model), "application/pdf");
    }
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter SiteReportsEndpointTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Verificar que la ruta nueva no secuestra rutas existentes**

`/{siteSlug}/reports` es una plantilla de dos segmentos con el primero variable, y el proyecto ya tiene rutas literales de dos segmentos como `/demo/{slug}`. ASP.NET Core prefiere segmentos literales sobre parámetros, así que no debería haber captura, pero hay que comprobarlo en vez de asumirlo.

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: sin fallos nuevos. En particular `PublicReportsEndpointTests` y `EmployeesEndpointTests` (que ejercitan `/api/public/...`) deben seguir igual que antes.

Si alguna ruta existente empieza a devolver el HTML de KPIs, reportarlo: significa que el orden de precedencia no es el supuesto y hay que restringir la plantilla (por ejemplo con una constraint sobre `siteSlug`).

- [ ] **Step 7: Commit**

```bash
git add src/Valentinos.Api/Controllers/DemoController.cs tests/Valentinos.Tests/Api/SiteReportsEndpointTests.cs
git commit -m "feat(reports): vista publica de KPIs por site en /{siteSlug}/reports"
```

---

### Task 3: Cerrar `/reports` y `/reports/pdf` con autenticación

**Files:**
- Modify: `src/Valentinos.Api/Controllers/DemoController.cs:442-443` y `:482-483` (atributos), más un helper nuevo
- Test: `tests/Valentinos.Tests/Api/ReportsAuthEndpointTests.cs`

**Interfaces:**
- Consumes: la ruta pública `GET /{siteSlug}/reports` de la Task 2 — uno de los tests comprueba que cerrar la vista multi-site no arrastró a la pública, así que la Task 2 debe estar hecha antes.
- Produces: nada.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Valentinos.Tests/Api/ReportsAuthEndpointTests.cs`:

```csharp
using System.Net;
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

// La vista multi-site (con el dropdown de sites) dejó de ser pública: sin sesión
// redirige al login, igual que el resto del panel.
public class ReportsAuthEndpointTests : IClassFixture<ReportsAuthEndpointTests.Factory>
{
    private readonly Factory _factory;
    public ReportsAuthEndpointTests(Factory factory) => _factory = factory;

    // Sin seguir el redirect: queremos ver el 302, no la página de login.
    private System.Net.Http.HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task GetReports_SinSesion_RedirigeAlLogin()
    {
        var resp = await NoRedirectClient().GetAsync("/reports");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/login", resp.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task GetReportsPdf_SinSesion_RedirigeAlLogin()
    {
        var resp = await NoRedirectClient().GetAsync("/reports/pdf");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/login", resp.Headers.Location?.OriginalString ?? "");
    }

    // El cierre de la vista multi-site no debe arrastrar a la pública por site.
    [Fact]
    public async Task GetSiteReports_SinSesion_SigueSiendoPublica()
    {
        var resp = await NoRedirectClient().GetAsync("/SoloUno/reports");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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
                    o.UseInMemoryDatabase("ReportsAuthEndpointTests"));
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

            tenantCtx.Set(tenant.Id);
            db.Sites.Add(new Site { Code = "001", Slug = "SoloUno" });
            db.SaveChanges();

            return host;
        }
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter ReportsAuthEndpointTests`
Expected: FAIL en los dos primeros — hoy `/reports` y `/reports/pdf` responden 200 sin sesión.

- [ ] **Step 3: Agregar el helper de coincidencia de tenant**

`AdminController` exige que el tenant del claim de la cookie coincida con el del subdominio (`AdminController.cs:35-41`). Al volver estas rutas autenticadas, conviene el mismo criterio en vez de dos reglas distintas para páginas autenticadas del mismo producto.

En `src/Valentinos.Api/Controllers/DemoController.cs`, agregar junto a la propiedad `Tid`:

```csharp
    // Defensa en profundidad para las rutas autenticadas de este controlador: el tenant
    // del claim de la cookie DEBE coincidir con el del subdominio. Las cookies son
    // host-only, así que el cruce no ocurre en condiciones normales; esto lo cierra igual,
    // con el mismo criterio que ya aplica AdminController.
    private bool TenantDelUsuarioCoincide()
    {
        var claim = User.FindFirstValue("tenant");
        return Guid.TryParse(claim, out var userTenant) && userTenant != Guid.Empty && userTenant == Tid;
    }
```

Si falta, agregar `using System.Security.Claims;` al tope del archivo.

- [ ] **Step 4: Cerrar las dos acciones privadas**

En la acción `KpiPage` (`/reports`), reemplazar `[AllowAnonymous]` por `[Authorize(Roles = "admin")]` y agregar la comprobación de tenant como primera línea del cuerpo:

```csharp
    [Authorize(Roles = "admin")]
    [HttpGet("/reports")]
    public async Task<IActionResult> KpiPage([FromQuery] string? site, [FromQuery] string? period)
    {
        if (!TenantDelUsuarioCoincide()) return Redirect("/login");

        var key = await ReportsSiteKeyAsync(site);
```

Hacer lo mismo en `KpiPdfPreview` (`/reports/pdf`):

```csharp
    [Authorize(Roles = "admin")]
    [HttpGet("/reports/pdf")]
    public async Task<IActionResult> KpiPdfPreview([FromQuery] string? site, [FromQuery] string? period)
    {
        if (!TenantDelUsuarioCoincide()) return Redirect("/login");

        var key = await ReportsSiteKeyAsync(site);
```

Actualizar también el comentario que precede a `KpiPage`, que hoy dice "Vista pública (solo lectura)":

```csharp
    // Vista PRIVADA (admin): dashboard de KPIs multi-site en /reports?site={slugOrGuid}.
    // Si no se pasa site, usa el site por defecto. Trae el dropdown con los sites del
    // tenant. La vista pública por site vive en /{siteSlug}/reports.
```

Si falta, agregar `using Microsoft.AspNetCore.Authorization;` al tope del archivo.

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj --filter ReportsAuthEndpointTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test tests/Valentinos.Tests/Valentinos.Tests.csproj`
Expected: sin fallos nuevos más allá del preexistente conocido.

- [ ] **Step 7: Commit**

```bash
git add src/Valentinos.Api/Controllers/DemoController.cs tests/Valentinos.Tests/Api/ReportsAuthEndpointTests.cs
git commit -m "feat(reports): /reports y /reports/pdf pasan a requerir login de admin"
```

---

### Task 4: Verificación visual (sin enviar correos)

**Files:** ninguno (solo revisión).

- [ ] **Step 1: Arrancar la app con SMTP apagado**

```powershell
$env:Smtp__Enabled = "false"; dotnet run --project src/Valentinos.Api/Valentinos.Api.csproj
```

El `DailyReportScheduler` está registrado siempre; con `Smtp:Enabled=false` (que gana sobre los user-secrets) `SmtpEmailSender.SendAsync` retorna sin conectar aunque llegue la hora.

- [ ] **Step 2: Revisar la vista pública**

El ruteo resuelve el tenant por subdominio, así que en local hay que mandar el `Host`:

```bash
curl -s -H "Host: mastercorp.valentisoft.com" "http://localhost:5270/Kp7Qm/reports" -o publico.html
```

Verificar en `publico.html`: NO aparece `id="site"`, NO aparece `XjUS3` ni `Site 069`; SÍ aparecen el selector de periodo, el botón de PDF y las banderas ES/EN.

- [ ] **Step 3: Revisar el PDF público**

```bash
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" -H "Host: mastercorp.valentisoft.com" "http://localhost:5270/Kp7Qm/reports/pdf"
```
Expected: `200 application/pdf`.

- [ ] **Step 4: Revisar que la privada pide login**

```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "Host: mastercorp.valentisoft.com" "http://localhost:5270/reports"
curl -s -o /dev/null -w "%{http_code}\n" -H "Host: mastercorp.valentisoft.com" "http://localhost:5270/reports/pdf"
```
Expected: `302` en ambas.

- [ ] **Step 5: Revisar la privada ya autenticado**

Entrar por navegador a `/login`, iniciar sesión y abrir `/reports`. Verificar que el dropdown de sites aparece, que cambiar de site navega correctamente y que el botón de PDF descarga.

- [ ] **Step 6: Detener la app**

Cortar el proceso. No dejarla corriendo: con los user-secrets normales vuelve a tener SMTP activo.

---

## Notas de riesgo

- **La precedencia de rutas es la única suposición del plan que no se puede verificar leyendo el código.** El Step 6 de la Task 2 existe para comprobarla con la suite completa.
- **Los links `/reports?site=X` ya compartidos pasan a pedir login.** Es el resultado aceptado en el diseño, pero conviene avisar a quien los tenga guardados.
- **La vista pública queda protegida solo por el slug.** Es la misma barrera que ya protege los formularios impresos en los QR, y fue una decisión explícita del usuario.
