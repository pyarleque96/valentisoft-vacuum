# Plan 1 — Fundación & Multitenancy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Levantar la solución backend .NET con EF Core + SQL Server, la entidad Tenant, resolución de tenant por slug de URL, aislamiento multitenant vía global query filter, y seed del tenant MasterCorp — todo verificable con un endpoint `whoami`.

**Architecture:** Solución .NET 10 en 4 proyectos por capas (Api, Domain, Application, Infrastructure) + proyecto de tests. El tenant actual del request se resuelve en un middleware a partir del slug de la ruta (`/r/{slug}/...` y `/api/public/{slug}/...`) y se expone vía `ITenantContext`. EF Core aplica un global query filter por `TenantId` para que ningún query de negocio se ejecute sin filtrar.

**Tech Stack:** .NET 10, ASP.NET Core Web API (controllers), EF Core 10, SQL Server, xUnit, Microsoft.AspNetCore.Mvc.Testing.

## Global Constraints

- Target framework: **net10.0** en todos los proyectos.
- Base de datos: **SQL Server** (LocalDB en desarrollo: `Server=(localdb)\\MSSQLLocalDB;Database=Valentinos;Trusted_Connection=True;TrustServerCertificate=True`).
- Toda entidad de negocio incluye `TenantId` (Guid) y hereda de una base común.
- Namespaces con raíz `Valentinos.*`.
- Nombre del solution: `Valentinos.sln` en la raíz del repo.
- El código de negocio con lógica (resolución de tenant, filtros) se escribe con **TDD**: test que falla → mínima implementación → test pasa → commit.
- Mensajes de commit en español, imperativo, prefijo convencional (`feat:`, `test:`, `chore:`).

---

### Task 1: Scaffolding de la solución

**Files:**
- Create: `Valentinos.sln`
- Create: `src/Valentinos.Domain/Valentinos.Domain.csproj`
- Create: `src/Valentinos.Application/Valentinos.Application.csproj`
- Create: `src/Valentinos.Infrastructure/Valentinos.Infrastructure.csproj`
- Create: `src/Valentinos.Api/Valentinos.Api.csproj`
- Create: `tests/Valentinos.Tests/Valentinos.Tests.csproj`

**Interfaces:**
- Consumes: nada (primera task).
- Produces: la estructura de proyectos y referencias que todas las tasks posteriores usan. Cadena de referencias: `Api → Application, Infrastructure`; `Infrastructure → Application → Domain`; `Tests → Api, Application, Infrastructure, Domain`.

- [ ] **Step 1: Crear solución y proyectos**

Ejecutar desde la raíz del repo (`C:/Users/pdro4/sources/IA/Ramces/Valentinos`):

```bash
dotnet new sln -n Valentinos
dotnet new classlib -n Valentinos.Domain -o src/Valentinos.Domain -f net10.0
dotnet new classlib -n Valentinos.Application -o src/Valentinos.Application -f net10.0
dotnet new classlib -n Valentinos.Infrastructure -o src/Valentinos.Infrastructure -f net10.0
dotnet new webapi -n Valentinos.Api -o src/Valentinos.Api -f net10.0 --use-controllers
dotnet new xunit -n Valentinos.Tests -o tests/Valentinos.Tests -f net10.0
```

Borrar los archivos `Class1.cs` que crean los classlib:

```bash
rm -f src/Valentinos.Domain/Class1.cs src/Valentinos.Application/Class1.cs src/Valentinos.Infrastructure/Class1.cs
```

- [ ] **Step 2: Agregar proyectos a la solución**

```bash
dotnet sln add src/Valentinos.Domain src/Valentinos.Application src/Valentinos.Infrastructure src/Valentinos.Api tests/Valentinos.Tests
```

- [ ] **Step 3: Configurar referencias entre proyectos**

```bash
dotnet add src/Valentinos.Application reference src/Valentinos.Domain
dotnet add src/Valentinos.Infrastructure reference src/Valentinos.Application
dotnet add src/Valentinos.Api reference src/Valentinos.Application src/Valentinos.Infrastructure
dotnet add tests/Valentinos.Tests reference src/Valentinos.Api src/Valentinos.Application src/Valentinos.Infrastructure src/Valentinos.Domain
```

- [ ] **Step 4: Agregar el paquete de integration testing a Tests**

```bash
dotnet add tests/Valentinos.Tests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 5: Compilar toda la solución**

Run: `dotnet build Valentinos.sln`
Expected: `Build succeeded` con 0 errores.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffolding de solución .NET por capas"
```

---

### Task 2: Entidad base y Tenant en Domain

**Files:**
- Create: `src/Valentinos.Domain/Common/BaseEntity.cs`
- Create: `src/Valentinos.Domain/Common/ITenantOwned.cs`
- Create: `src/Valentinos.Domain/Entities/Tenant.cs`
- Test: `tests/Valentinos.Tests/Domain/TenantTests.cs`

**Interfaces:**
- Consumes: nada del código propio.
- Produces:
  - `abstract class BaseEntity { Guid Id { get; set; } DateTime CreatedAt { get; set; } DateTime UpdatedAt { get; set; } }`
  - `interface ITenantOwned { Guid TenantId { get; set; } }`
  - `class Tenant : BaseEntity { string Slug; string Nombre; string? LogoUrl; }`
  - Método `Tenant.Create(string slug, string nombre)` que valida y normaliza el slug a minúsculas sin espacios.

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Domain/TenantTests.cs`:

```csharp
using Valentinos.Domain.Entities;
using Xunit;

namespace Valentinos.Tests.Domain;

public class TenantTests
{
    [Fact]
    public void Create_NormalizaSlugAMinusculasSinEspacios()
    {
        var tenant = Tenant.Create("  Master Corp ", "MasterCorp");

        Assert.Equal("master-corp", tenant.Slug);
        Assert.Equal("MasterCorp", tenant.Nombre);
        Assert.NotEqual(Guid.Empty, tenant.Id);
    }

    [Fact]
    public void Create_SlugVacio_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create("   ", "MasterCorp"));
    }
}
```

- [ ] **Step 2: Ejecutar el test para verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~TenantTests`
Expected: FAIL de compilación — `Tenant` no existe.

- [ ] **Step 3: Implementar entidades**

`src/Valentinos.Domain/Common/BaseEntity.cs`:

```csharp
namespace Valentinos.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

`src/Valentinos.Domain/Common/ITenantOwned.cs`:

```csharp
namespace Valentinos.Domain.Common;

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
```

`src/Valentinos.Domain/Entities/Tenant.cs`:

```csharp
using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }

    public static Tenant Create(string slug, string nombre)
    {
        var normalizado = (slug ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "-");
        if (string.IsNullOrWhiteSpace(normalizado))
            throw new ArgumentException("El slug no puede estar vacío.", nameof(slug));

        return new Tenant { Slug = normalizado, Nombre = nombre };
    }
}
```

- [ ] **Step 4: Ejecutar el test para verificar que pasa**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~TenantTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: entidad Tenant con normalización de slug"
```

---

### Task 3: DbContext, EF Core y global query filter

**Files:**
- Create: `src/Valentinos.Application/Abstractions/ITenantContext.cs`
- Create: `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/Valentinos.Infrastructure/DependencyInjection.cs`
- Modify: `src/Valentinos.Infrastructure/Valentinos.Infrastructure.csproj` (paquetes EF Core)
- Test: `tests/Valentinos.Tests/Persistence/TenantFilterTests.cs`

**Interfaces:**
- Consumes: `Tenant`, `ITenantOwned`, `BaseEntity` (Task 2).
- Produces:
  - `interface ITenantContext { Guid? TenantId { get; } void Set(Guid tenantId); }`
  - `class AppDbContext : DbContext` con `DbSet<Tenant> Tenants` y un global query filter aplicado a toda entidad que implemente `ITenantOwned`, comparando `TenantId == _tenantContext.TenantId`.
  - `static IServiceCollection AddInfrastructure(this IServiceCollection, string connectionString)`.

**Nota de test:** para no depender de SQL Server en tests unitarios, `TenantFilterTests` usa el provider **InMemory** de EF Core inyectando un `AppDbContext` con un `ITenantContext` de prueba. El paquete InMemory se agrega a Tests.

- [ ] **Step 1: Agregar paquetes EF Core**

```bash
dotnet add src/Valentinos.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/Valentinos.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add tests/Valentinos.Tests package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 2: Crear la abstracción ITenantContext**

`src/Valentinos.Application/Abstractions/ITenantContext.cs`:

```csharp
namespace Valentinos.Application.Abstractions;

public interface ITenantContext
{
    Guid? TenantId { get; }
    void Set(Guid tenantId);
}
```

- [ ] **Step 3: Escribir el test que falla**

`tests/Valentinos.Tests/Persistence/TenantFilterTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Common;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Persistence;

// ITenantContext mutable de prueba
public class FakeTenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public void Set(Guid tenantId) => TenantId = tenantId;
}

// Entidad de prueba tenant-owned para validar el filtro global
public class Widget : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class TenantFilterTests
{
    private static AppDbContext BuildContext(ITenantContext ctx)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options, ctx);
    }

    [Fact]
    public void QueryFilter_SoloDevuelveFilasDelTenantActual()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var ctx = new FakeTenantContext();

        using (var db = BuildContext(ctx))
        {
            db.Set<Widget>().Add(new Widget { TenantId = tenantA, Nombre = "A" });
            db.Set<Widget>().Add(new Widget { TenantId = tenantB, Nombre = "B" });
            db.SaveChanges();
        }

        ctx.Set(tenantA);
        using (var db = BuildContext(ctx))
        {
            // recrear datos en la misma inmemory db no aplica; usamos db compartida abajo
        }
    }
}

// DbContext de test que registra Widget además de las entidades reales
public class TestDbContext : AppDbContext
{
    public TestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    public DbSet<Widget> Widgets => Set<Widget>();
}
```

> Nota para el implementador: el InMemory provider crea una BD nueva por nombre. Ajusta el test para compartir el mismo nombre de BD entre inserción y lectura. La versión final del test está en el Step 5; primero confirma que NO compila porque `AppDbContext` aún no existe.

- [ ] **Step 4: Verificar que el test falla por compilación**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~TenantFilterTests`
Expected: FAIL — `AppDbContext` no existe.

- [ ] **Step 5: Implementar AppDbContext con el global filter y reescribir el test correctamente**

`src/Valentinos.Infrastructure/Persistence/AppDbContext.cs`:

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Common;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            e.Property(t => t.Nombre).HasMaxLength(200).IsRequired();
        });

        // Global query filter por TenantId para toda entidad ITenantOwned
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                var param = Expression.Parameter(entityType.ClrType, "e");
                var tenantIdProp = Expression.Property(param, nameof(ITenantOwned.TenantId));
                var currentTenant = Expression.Property(
                    Expression.Constant(this), nameof(CurrentTenantId));
                var body = Expression.Equal(tenantIdProp, currentTenant);
                var lambda = Expression.Lambda(body, param);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }

    // Usado por el query filter; Guid.Empty cuando no hay tenant (no matchea nada)
    public Guid CurrentTenantId => _tenantContext.TenantId ?? Guid.Empty;

    public override int SaveChanges()
    {
        StampTenantAndTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenantAndTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampTenantAndTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is BaseEntity be && entry.State == EntityState.Modified)
                be.UpdatedAt = DateTime.UtcNow;

            if (entry.State == EntityState.Added
                && entry.Entity is ITenantOwned owned
                && owned.TenantId == Guid.Empty
                && _tenantContext.TenantId is Guid tid)
            {
                owned.TenantId = tid;
            }
        }
    }
}
```

`src/Valentinos.Infrastructure/DependencyInjection.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));
        return services;
    }
}
```

Reescribir `tests/Valentinos.Tests/Persistence/TenantFilterTests.cs` con la versión definitiva (comparte nombre de BD entre escritura y lectura):

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Common;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Persistence;

public class FakeTenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public void Set(Guid tenantId) => TenantId = tenantId;
}

public class Widget : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class TestDbContext : AppDbContext
{
    public TestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext) { }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Widget>();
    }
}

public class TenantFilterTests
{
    [Fact]
    public void QueryFilter_SoloDevuelveFilasDelTenantActual()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options;

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Semilla con ambos tenants (filtro desactivado escribiendo TenantId explícito)
        var seedCtx = new FakeTenantContext();
        using (var db = new TestDbContext(options, seedCtx))
        {
            db.Widgets.Add(new Widget { TenantId = tenantA, Nombre = "A" });
            db.Widgets.Add(new Widget { TenantId = tenantB, Nombre = "B" });
            db.SaveChanges();
        }

        // Lectura como tenant A: solo debe ver "A"
        var readCtx = new FakeTenantContext();
        readCtx.Set(tenantA);
        using (var db = new TestDbContext(options, readCtx))
        {
            var visibles = db.Widgets.ToList();
            Assert.Single(visibles);
            Assert.Equal("A", visibles[0].Nombre);
        }
    }

    [Fact]
    public void SaveChanges_EstampaTenantIdAutomaticamente()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options;

        var tenant = Guid.NewGuid();
        var ctx = new FakeTenantContext();
        ctx.Set(tenant);

        using var db = new TestDbContext(options, ctx);
        db.Widgets.Add(new Widget { Nombre = "SinTenantExplicito" });
        db.SaveChanges();

        Assert.Equal(tenant, db.Widgets.Single().TenantId);
    }
}
```

- [ ] **Step 6: Ejecutar los tests para verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~TenantFilterTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: AppDbContext con global query filter y estampado de TenantId"
```

---

### Task 4: Resolución de tenant por slug (middleware)

**Files:**
- Create: `src/Valentinos.Api/Multitenancy/TenantContext.cs`
- Create: `src/Valentinos.Api/Multitenancy/TenantResolutionMiddleware.cs`
- Test: `tests/Valentinos.Tests/Multitenancy/SlugExtractionTests.cs`

**Interfaces:**
- Consumes: `ITenantContext` (Task 3).
- Produces:
  - `class TenantContext : ITenantContext` (scoped, mutable).
  - `static class SlugResolver { static string? FromPath(string path); }` — extrae el slug de rutas `/r/{slug}/...` y `/api/public/{slug}/...`, devuelve `null` si no aplica.
  - `class TenantResolutionMiddleware` que, si hay slug, busca el `Tenant` por slug en `AppDbContext` y llama `ITenantContext.Set(tenant.Id)`.

- [ ] **Step 1: Escribir el test que falla (lógica pura de extracción de slug)**

`tests/Valentinos.Tests/Multitenancy/SlugExtractionTests.cs`:

```csharp
using Valentinos.Api.Multitenancy;
using Xunit;

namespace Valentinos.Tests.Multitenancy;

public class SlugExtractionTests
{
    [Theory]
    [InlineData("/r/mastercorp/VAC-001", "mastercorp")]
    [InlineData("/api/public/mastercorp/reports", "mastercorp")]
    [InlineData("/api/public/master-corp/reports", "master-corp")]
    [InlineData("/admin/dashboard", null)]
    [InlineData("/", null)]
    [InlineData("/r/", null)]
    public void FromPath_ExtraeSlugCorrecto(string path, string? esperado)
    {
        Assert.Equal(esperado, SlugResolver.FromPath(path));
    }
}
```

- [ ] **Step 2: Verificar que el test falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SlugExtractionTests`
Expected: FAIL — `SlugResolver` no existe.

- [ ] **Step 3: Implementar TenantContext, SlugResolver y middleware**

`src/Valentinos.Api/Multitenancy/TenantContext.cs`:

```csharp
using Valentinos.Application.Abstractions;

namespace Valentinos.Api.Multitenancy;

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public void Set(Guid tenantId) => TenantId = tenantId;
}
```

`src/Valentinos.Api/Multitenancy/TenantResolutionMiddleware.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Multitenancy;

public static class SlugResolver
{
    // Extrae el slug de /r/{slug}/... y /api/public/{slug}/...
    public static string? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var segs = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segs.Length >= 2 && segs[0] == "r")
            return segs[1];
        if (segs.Length >= 3 && segs[0] == "api" && segs[1] == "public")
            return segs[2];
        return null;
    }
}

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenantContext, AppDbContext db)
    {
        var slug = SlugResolver.FromPath(ctx.Request.Path.Value ?? string.Empty);
        if (slug is not null)
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Slug == slug);
            if (tenant is not null)
                tenantContext.Set(tenant.Id);
        }
        await _next(ctx);
    }
}
```

- [ ] **Step 4: Verificar que el test pasa**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SlugExtractionTests`
Expected: PASS (6 casos).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: resolución de tenant por slug de URL vía middleware"
```

---

### Task 5: Wire-up en Program.cs, seed de MasterCorp y endpoint whoami

**Files:**
- Modify: `src/Valentinos.Api/Program.cs` (reemplazo completo)
- Create: `src/Valentinos.Api/Controllers/PublicTenantController.cs`
- Create: `src/Valentinos.Infrastructure/Persistence/DbSeeder.cs`
- Modify: `src/Valentinos.Api/appsettings.json` (connection string)
- Test: `tests/Valentinos.Tests/Api/WhoAmIEndpointTests.cs`

**Interfaces:**
- Consumes: `AddInfrastructure` (Task 3), `TenantContext`, `TenantResolutionMiddleware`, `SlugResolver` (Task 4), `ITenantContext`, `Tenant`.
- Produces:
  - Endpoint `GET /api/public/{slug}/whoami` → `200 { tenantId, slug, nombre }` si el tenant existe; `404` si no.
  - `static Task DbSeeder.SeedAsync(AppDbContext db)` que crea MasterCorp si no existe.

- [ ] **Step 1: Configurar la connection string**

En `src/Valentinos.Api/appsettings.json` agregar:

```json
"ConnectionStrings": {
  "Default": "Server=(localdb)\\MSSQLLocalDB;Database=Valentinos;Trusted_Connection=True;TrustServerCertificate=True"
}
```

- [ ] **Step 2: Crear el seeder**

`src/Valentinos.Infrastructure/Persistence/DbSeeder.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        var existe = await db.Tenants.IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == "mastercorp");
        if (!existe)
        {
            db.Tenants.Add(Tenant.Create("mastercorp", "MasterCorp"));
            await db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 3: Crear el controller whoami**

`src/Valentinos.Api/Controllers/PublicTenantController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Controllers;

[ApiController]
[Route("api/public/{slug}")]
public class PublicTenantController : ControllerBase
{
    private readonly AppDbContext _db;
    public PublicTenantController(AppDbContext db) => _db = db;

    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI(string slug)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();
        return Ok(new { tenantId = tenant.Id, slug = tenant.Slug, nombre = tenant.Nombre });
    }
}
```

- [ ] **Step 4: Reescribir Program.cs**

`src/Valentinos.Api/Program.cs` (reemplazo completo):

```csharp
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
```

- [ ] **Step 5: Crear la migración inicial**

Requiere la herramienta EF: `dotnet tool install --global dotnet-ef` (si no está instalada).

```bash
dotnet ef migrations add InitialCreate \
  --project src/Valentinos.Infrastructure \
  --startup-project src/Valentinos.Api \
  --output-dir Persistence/Migrations
```

Expected: se crea la carpeta `Persistence/Migrations` con la migración.

- [ ] **Step 6: Escribir el test de integración**

`tests/Valentinos.Tests/Api/WhoAmIEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Api;

public class WhoAmIEndpointTests : IClassFixture<WhoAmIEndpointTests.Factory>
{
    private readonly Factory _factory;
    public WhoAmIEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task WhoAmI_TenantExistente_Devuelve200ConDatos()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/public/mastercorp/whoami");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WhoAmIResponse>();
        Assert.Equal("mastercorp", body!.Slug);
        Assert.Equal("MasterCorp", body.Nombre);
    }

    [Fact]
    public async Task WhoAmI_TenantInexistente_Devuelve404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/public/noexiste/whoami");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    public record WhoAmIResponse(Guid TenantId, string Slug, string Nombre);

    // Factory que reemplaza SQL Server por InMemory y siembra MasterCorp
    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(o =>
                    o.UseInMemoryDatabase("WhoAmITests"));

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                DbSeeder.SeedAsync(db).GetAwaiter().GetResult();
            });
        }
    }
}
```

- [ ] **Step 7: Ejecutar toda la suite**

Run: `dotnet test Valentinos.sln`
Expected: PASS — todos los tests (Tenant, TenantFilter, SlugExtraction, WhoAmI) verdes.

- [ ] **Step 8: Verificación manual (opcional pero recomendada)**

Con LocalDB disponible: `dotnet run --project src/Valentinos.Api`, luego navegar a
`http://localhost:<puerto>/api/public/mastercorp/whoami` y confirmar el JSON de MasterCorp.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: wire-up de API, migración inicial, seed MasterCorp y endpoint whoami"
```

---

## Self-Review

**Spec coverage (Plan 1 scope):**
- Solución .NET por capas → Task 1 ✓
- Entidad Tenant + slug → Task 2 ✓
- BD compartida + TenantId global filter → Task 3 ✓
- Resolución de tenant por slug de URL → Task 4 ✓
- Seed MasterCorp + verificación end-to-end → Task 5 ✓
- (Fuera de Plan 1, en planes siguientes: AssetType/Asset/QR, reportes, auth, KPIs, Hangfire, frontend.)

**Type consistency:** `ITenantContext` con `TenantId`/`Set` usado igual en Tasks 3-5. `AppDbContext(DbContextOptions<AppDbContext>, ITenantContext)` consistente en Infra y en los DbContext de test. `SlugResolver.FromPath` y `DbSeeder.SeedAsync` con las mismas firmas donde se consumen.

**Placeholders:** ninguno pendiente; el único "primer test provisional" del Task 3 se reemplaza explícitamente por la versión definitiva en el Step 5 del mismo task.

**Notas de entorno:** requiere .NET 10 SDK, `dotnet-ef` global, y SQL Server LocalDB para el arranque real (los tests no lo requieren: usan InMemory).
