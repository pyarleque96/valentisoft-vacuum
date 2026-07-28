# Plan 2 — Activos & QR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modelar tipos de activo y activos con generación de códigos correlativos atómicos por tenant (`VAC-001`), y un servicio de render de QR que produce una imagen PNG con el QR (apuntando a la URL pública de reporte), el código impreso y el logo del tenant como marca de agua, más una hoja PDF en lote para imprimir.

**Architecture:** Se construye sobre el Plan 1 (Domain/Application/Infrastructure/Api). Las **entidades** `AssetType` y `Asset` viven en `Valentinos.Domain` e implementan `ITenantOwned` (heredan el aislamiento multitenant y el write-path fail-closed ya existentes). La **lógica** (generación de código, CRUD) vive en servicios de `Valentinos.Application` con implementación en `Valentinos.Infrastructure`, consumiendo `AppDbContext` + `ITenantContext`. El **render de QR** es un servicio detrás de `IQrRenderer` (interfaz en Application, implementación en Infrastructure con QRCoder + SkiaSharp). **No se exponen endpoints HTTP en este plan**: los controllers de administración se agregan en el Plan 4 junto con la autenticación JWT. Todo se valida con tests unitarios/integración a nivel de servicio.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server / InMemory en tests), QRCoder, SkiaSharp (composición PNG + PDF), ZXing.Net.Bindings.SkiaSharp (solo en tests, para decodificar el QR y verificar), xUnit.

## Global Constraints

- Target framework: **net10.0**; namespaces raíz `Valentinos.*`.
- `AssetType` y `Asset` implementan `ITenantOwned` (`Guid TenantId`) y heredan de `BaseEntity`. El aislamiento por tenant y el estampado lo aplica `AppDbContext` (Plan 1) — **no** duplicar esa lógica.
- Código de activo: formato `PREFIJO-NNN` con **NNN como mínimo 3 dígitos, cero-padded** (`VAC-001`), y sin límite superior artificial (a partir de 1000 usa los dígitos que hagan falta: `VAC-1000`). La asignación es **atómica** (incrementa `AssetType.CorrelativoActual` y persiste en la misma transacción / `SaveChanges`).
- Unicidad garantizada por índices EF: `AssetType` único por `(TenantId, Prefijo)`; `Asset` único por `(TenantId, Codigo)`.
- La URL que codifica el QR es `{BaseUrl}/r/{slug}/{codigo}` (ej. `https://app.valentinos.com/r/mastercorp/VAC-001`). `BaseUrl` es configurable (`QrOptions.BaseUrl`), default `https://app.valentinos.com`.
- El QR se genera con **ECC nivel H** (alta corrección de errores) para que el logo central no impida la lectura.
- Los servicios se registran en el contenedor DI vía `AddInfrastructure` (Plan 1) o un método de extensión que este invoque.
- Lógica de negocio con **TDD**: test que falla → implementación mínima → test pasa → commit. Los tests de render usan **decodificación real** del QR como criterio de aceptación (no solo "el PNG no está vacío").
- **Adaptación de APIs de terceros:** el código de QRCoder/SkiaSharp/ZXing en este plan es la implementación de referencia. Si la versión instalada difiere en una firma exacta, el implementador ajusta la llamada para lograr el **comportamiento verificado por el test** (el test de decodificación/validez es la fuente de verdad), y lo documenta en su reporte.
- Migraciones EF: seguir las reglas de seguridad del agente `sr-net-developer` (nunca `--no-build`; verificar el `.cs` generado; no aplicar a la BD sin autorización).
- Mensajes de commit en español, imperativo, prefijo convencional.

---

### Task 1: Entidades AssetType y Asset + migración

**Files:**
- Create: `src/Valentinos.Domain/Entities/AssetType.cs`
- Create: `src/Valentinos.Domain/Entities/Asset.cs`
- Create: `src/Valentinos.Domain/Enums/AssetEstado.cs`
- Modify: `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs` (agregar DbSets + config)
- Create: migración `AddAssetTypeAndAsset` en `src/Valentinos.Infrastructure/Persistence/Migrations`
- Test: `tests/Valentinos.Tests/Domain/AssetTypeTests.cs`

**Interfaces:**
- Consumes: `BaseEntity`, `ITenantOwned` (Plan 1); `AppDbContext`.
- Produces:
  - `enum AssetEstado { Activo, Baja }`
  - `class AssetType : BaseEntity, ITenantOwned { Guid TenantId; string Nombre; string Prefijo; int CorrelativoActual; }` con `static AssetType Create(string nombre, string prefijo)` que normaliza el prefijo a **mayúsculas sin espacios** y valida no-vacío.
  - `class Asset : BaseEntity, ITenantOwned { Guid TenantId; Guid AssetTypeId; string Codigo; AssetEstado Estado; string? Ubicacion; }`
  - `AppDbContext.AssetTypes` y `AppDbContext.Assets` (DbSets) + índices únicos.

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Domain/AssetTypeTests.cs`:

```csharp
using Valentinos.Domain.Entities;
using Xunit;

namespace Valentinos.Tests.Domain;

public class AssetTypeTests
{
    [Fact]
    public void Create_NormalizaPrefijoAMayusculasSinEspacios()
    {
        var t = AssetType.Create("Aspiradora", "  vac ");

        Assert.Equal("Aspiradora", t.Nombre);
        Assert.Equal("VAC", t.Prefijo);
        Assert.Equal(0, t.CorrelativoActual);
        Assert.NotEqual(Guid.Empty, t.Id);
    }

    [Fact]
    public void Create_PrefijoVacio_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => AssetType.Create("Aspiradora", "   "));
    }
}
```

- [ ] **Step 2: Ejecutar el test para verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~AssetTypeTests`
Expected: FAIL de compilación — `AssetType` no existe.

- [ ] **Step 3: Implementar enum y entidades**

`src/Valentinos.Domain/Enums/AssetEstado.cs`:

```csharp
namespace Valentinos.Domain.Enums;

public enum AssetEstado
{
    Activo = 0,
    Baja = 1
}
```

`src/Valentinos.Domain/Entities/AssetType.cs`:

```csharp
using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class AssetType : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Prefijo { get; set; } = string.Empty;
    public int CorrelativoActual { get; set; }

    public static AssetType Create(string nombre, string prefijo)
    {
        var normalizado = (prefijo ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(normalizado))
            throw new ArgumentException("El prefijo no puede estar vacío.", nameof(prefijo));

        return new AssetType { Nombre = nombre, Prefijo = normalizado, CorrelativoActual = 0 };
    }
}
```

`src/Valentinos.Domain/Entities/Asset.cs`:

```csharp
using Valentinos.Domain.Common;
using Valentinos.Domain.Enums;

namespace Valentinos.Domain.Entities;

public class Asset : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AssetTypeId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public AssetEstado Estado { get; set; } = AssetEstado.Activo;
    public string? Ubicacion { get; set; }
}
```

- [ ] **Step 4: Registrar DbSets y configuración en AppDbContext**

En `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs`, agregar los `using` necesarios (`Valentinos.Domain.Enums` ya no hace falta si no se referencia el enum aquí) y dentro de la clase, junto a `DbSet<Tenant> Tenants`:

```csharp
    public DbSet<AssetType> AssetTypes => Set<AssetType>();
    public DbSet<Asset> Assets => Set<Asset>();
```

Y dentro de `OnModelCreating`, **después** del bloque `modelBuilder.Entity<Tenant>(...)` y **antes** del `foreach` del global query filter:

```csharp
        modelBuilder.Entity<AssetType>(e =>
        {
            e.Property(t => t.Nombre).HasMaxLength(200).IsRequired();
            e.Property(t => t.Prefijo).HasMaxLength(20).IsRequired();
            e.HasIndex(t => new { t.TenantId, t.Prefijo }).IsUnique();
        });

        modelBuilder.Entity<Asset>(e =>
        {
            e.Property(a => a.Codigo).HasMaxLength(40).IsRequired();
            e.Property(a => a.Ubicacion).HasMaxLength(200);
            e.HasIndex(a => new { a.TenantId, a.Codigo }).IsUnique();
            e.HasOne<AssetType>()
             .WithMany()
             .HasForeignKey(a => a.AssetTypeId)
             .OnDelete(DeleteBehavior.Restrict);
        });
```

> Nota: `AssetType` y `Asset` implementan `ITenantOwned`, por lo que el `foreach` existente les aplicará automáticamente el global query filter por `TenantId`. No agregar filtros manuales.

- [ ] **Step 5: Ejecutar el test de dominio para verificar que pasa**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~AssetTypeTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Generar la migración**

```bash
dotnet ef migrations add AddAssetTypeAndAsset \
  --project src/Valentinos.Infrastructure \
  --startup-project src/Valentinos.Api \
  --output-dir Persistence/Migrations
```

Abrir el `.cs` generado y verificar: crea tablas `AssetTypes` y `Assets`, con índices únicos `IX_AssetTypes_TenantId_Prefijo` e `IX_Assets_TenantId_Codigo`, FK `Assets.AssetTypeId → AssetTypes.Id` con `OnDelete: Restrict`, **sin shadow FKs** (ej. `AssetTypeId1`). No aplicar a la BD.

- [ ] **Step 7: Compilar y correr la suite completa**

Run: `dotnet test Valentinos.sln`
Expected: PASS — todos los tests previos + los nuevos verdes.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: entidades AssetType y Asset con índices únicos por tenant y migración"
```

---

### Task 2: Servicio de creación de activos con código correlativo atómico

**Files:**
- Create: `src/Valentinos.Application/Assets/IAssetService.cs`
- Create: `src/Valentinos.Application/Assets/AssetDtos.cs`
- Create: `src/Valentinos.Infrastructure/Assets/AssetService.cs`
- Modify: `src/Valentinos.Infrastructure/DependencyInjection.cs` (registrar el servicio)
- Test: `tests/Valentinos.Tests/Assets/AssetServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `ITenantContext`, `AssetType`, `Asset`, `AssetEstado`.
- Produces:
  - `record CreateAssetTypeRequest(string Nombre, string Prefijo);`
  - `record CreateAssetRequest(Guid AssetTypeId, string? Ubicacion);`
  - `record AssetTypeDto(Guid Id, string Nombre, string Prefijo, int CorrelativoActual);`
  - `record AssetDto(Guid Id, Guid AssetTypeId, string Codigo, string Estado, string? Ubicacion);`
  - `interface IAssetService { Task<AssetTypeDto> CreateAssetTypeAsync(CreateAssetTypeRequest req, CancellationToken ct = default); Task<IReadOnlyList<AssetTypeDto>> ListAssetTypesAsync(CancellationToken ct = default); Task<AssetDto> CreateAssetAsync(CreateAssetRequest req, CancellationToken ct = default); Task<IReadOnlyList<AssetDto>> ListAssetsAsync(CancellationToken ct = default); }`
  - `AssetService.FormatCodigo(string prefijo, int correlativo)` → `PREFIJO-NNN` (público estático para poder testearlo en aislamiento).
  - Registro DI: `services.AddScoped<IAssetService, AssetService>();`

**Nota de test:** el provider **InMemory** no soporta transacciones reales; los tests verifican la **corrección secuencial** (VAC-001, VAC-002), la **independencia de contadores** entre tipos, el **formato/zero-padding**, y que el código respeta el prefijo del tipo. La atomicidad bajo concurrencia real (SQL Server) queda cubierta por el índice único `(TenantId, Codigo)` + la estrategia transaccional del servicio; documentarlo como limitación del test InMemory.

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Assets/AssetServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Assets;
using Valentinos.Infrastructure.Assets;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Assets;

public class AssetServiceTests
{
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    private static (AppDbContext db, IAssetService svc) Build(Guid tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ctx = new FixedTenantContext();
        ctx.Set(tenant);
        var db = new AppDbContext(options, ctx);
        return (db, new AssetService(db, ctx));
    }

    [Fact]
    public void FormatCodigo_AplicaZeroPaddingDeTresDigitos()
    {
        Assert.Equal("VAC-001", AssetService.FormatCodigo("VAC", 1));
        Assert.Equal("VAC-042", AssetService.FormatCodigo("VAC", 42));
        Assert.Equal("VAC-1000", AssetService.FormatCodigo("VAC", 1000));
    }

    [Fact]
    public async Task CreateAssetAsync_GeneraCodigosCorrelativos()
    {
        var (_, svc) = Build(Guid.NewGuid());
        var tipo = await svc.CreateAssetTypeAsync(new CreateAssetTypeRequest("Aspiradora", "VAC"));

        var a1 = await svc.CreateAssetAsync(new CreateAssetRequest(tipo.Id, "Piso 1"));
        var a2 = await svc.CreateAssetAsync(new CreateAssetRequest(tipo.Id, "Piso 2"));

        Assert.Equal("VAC-001", a1.Codigo);
        Assert.Equal("VAC-002", a2.Codigo);
        Assert.Equal("Activo", a1.Estado);
    }

    [Fact]
    public async Task CreateAssetAsync_ContadoresIndependientesPorTipo()
    {
        var (_, svc) = Build(Guid.NewGuid());
        var vac = await svc.CreateAssetTypeAsync(new CreateAssetTypeRequest("Aspiradora", "VAC"));
        var car = await svc.CreateAssetTypeAsync(new CreateAssetTypeRequest("Carrito", "CAR"));

        var v1 = await svc.CreateAssetAsync(new CreateAssetRequest(vac.Id, null));
        var c1 = await svc.CreateAssetAsync(new CreateAssetRequest(car.Id, null));
        var v2 = await svc.CreateAssetAsync(new CreateAssetRequest(vac.Id, null));

        Assert.Equal("VAC-001", v1.Codigo);
        Assert.Equal("CAR-001", c1.Codigo);
        Assert.Equal("VAC-002", v2.Codigo);
    }

    [Fact]
    public async Task CreateAssetAsync_TipoInexistente_LanzaExcepcion()
    {
        var (_, svc) = Build(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAssetAsync(new CreateAssetRequest(Guid.NewGuid(), null)));
    }
}
```

- [ ] **Step 2: Ejecutar el test para verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~AssetServiceTests`
Expected: FAIL de compilación — `IAssetService`/`AssetService` no existen.

- [ ] **Step 3: Definir DTOs e interfaz en Application**

`src/Valentinos.Application/Assets/AssetDtos.cs`:

```csharp
namespace Valentinos.Application.Assets;

public record CreateAssetTypeRequest(string Nombre, string Prefijo);
public record CreateAssetRequest(Guid AssetTypeId, string? Ubicacion);

public record AssetTypeDto(Guid Id, string Nombre, string Prefijo, int CorrelativoActual);
public record AssetDto(Guid Id, Guid AssetTypeId, string Codigo, string Estado, string? Ubicacion);
```

`src/Valentinos.Application/Assets/IAssetService.cs`:

```csharp
namespace Valentinos.Application.Assets;

public interface IAssetService
{
    Task<AssetTypeDto> CreateAssetTypeAsync(CreateAssetTypeRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<AssetTypeDto>> ListAssetTypesAsync(CancellationToken ct = default);
    Task<AssetDto> CreateAssetAsync(CreateAssetRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<AssetDto>> ListAssetsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar AssetService en Infrastructure**

`src/Valentinos.Infrastructure/Assets/AssetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Assets;
using Valentinos.Domain.Entities;
using Valentinos.Domain.Enums;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure.Assets;

public class AssetService : IAssetService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public AssetService(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public static string FormatCodigo(string prefijo, int correlativo)
        => $"{prefijo}-{correlativo.ToString("D3")}";

    public async Task<AssetTypeDto> CreateAssetTypeAsync(CreateAssetTypeRequest req, CancellationToken ct = default)
    {
        var tipo = AssetType.Create(req.Nombre, req.Prefijo);
        _db.AssetTypes.Add(tipo);
        await _db.SaveChangesAsync(ct);
        return ToDto(tipo);
    }

    public async Task<IReadOnlyList<AssetTypeDto>> ListAssetTypesAsync(CancellationToken ct = default)
        => await _db.AssetTypes.OrderBy(t => t.Prefijo)
            .Select(t => ToDto(t)).ToListAsync(ct);

    public async Task<AssetDto> CreateAssetAsync(CreateAssetRequest req, CancellationToken ct = default)
    {
        // El global query filter garantiza que solo se resuelve un tipo del tenant actual.
        var tipo = await _db.AssetTypes.FirstOrDefaultAsync(t => t.Id == req.AssetTypeId, ct)
            ?? throw new InvalidOperationException("El tipo de activo no existe para este tenant.");

        // Asignación atómica del correlativo: se incrementa el contador del tipo y se
        // persiste junto con el nuevo activo en un solo SaveChanges (misma transacción).
        tipo.CorrelativoActual += 1;
        var codigo = FormatCodigo(tipo.Prefijo, tipo.CorrelativoActual);

        var asset = new Asset
        {
            AssetTypeId = tipo.Id,
            Codigo = codigo,
            Estado = AssetEstado.Activo,
            Ubicacion = req.Ubicacion
        };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return ToDto(asset);
    }

    public async Task<IReadOnlyList<AssetDto>> ListAssetsAsync(CancellationToken ct = default)
        => await _db.Assets.OrderBy(a => a.Codigo)
            .Select(a => ToDto(a)).ToListAsync(ct);

    private static AssetTypeDto ToDto(AssetType t)
        => new(t.Id, t.Nombre, t.Prefijo, t.CorrelativoActual);

    private static AssetDto ToDto(Asset a)
        => new(a.Id, a.AssetTypeId, a.Codigo, a.Estado.ToString(), a.Ubicacion);
}
```

- [ ] **Step 5: Registrar el servicio en DI**

En `src/Valentinos.Infrastructure/DependencyInjection.cs`, agregar `using Valentinos.Application.Assets;` y `using Valentinos.Infrastructure.Assets;`, y dentro de `AddInfrastructure`, antes de `return services;`:

```csharp
        services.AddScoped<IAssetService, AssetService>();
```

- [ ] **Step 6: Ejecutar los tests del servicio para verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~AssetServiceTests`
Expected: PASS (4 tests).

- [ ] **Step 7: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (todo verde). Luego:

```bash
git add -A
git commit -m "feat: servicio de activos con generación de códigos correlativos por tipo"
```

---

### Task 3: Render de QR con código impreso y logo (PNG)

**Files:**
- Create: `src/Valentinos.Application/Qr/IQrRenderer.cs`
- Create: `src/Valentinos.Application/Qr/QrOptions.cs`
- Create: `src/Valentinos.Infrastructure/Qr/SkiaQrRenderer.cs`
- Modify: `src/Valentinos.Infrastructure/Valentinos.Infrastructure.csproj` (paquetes QRCoder, SkiaSharp)
- Modify: `src/Valentinos.Infrastructure/DependencyInjection.cs` (registrar el renderer + opciones)
- Modify: `tests/Valentinos.Tests/Valentinos.Tests.csproj` (paquete ZXing.Net.Bindings.SkiaSharp)
- Test: `tests/Valentinos.Tests/Qr/SkiaQrRendererTests.cs`

**Interfaces:**
- Consumes: nada del Plan 1 (servicio puro).
- Produces:
  - `class QrOptions { string BaseUrl { get; set; } = "https://app.valentinos.com"; }`
  - `record QrRenderRequest(string Slug, string Codigo, byte[]? LogoPng);`
  - `interface IQrRenderer { byte[] RenderPng(QrRenderRequest req); string BuildUrl(string slug, string codigo); }`
  - `SkiaQrRenderer` implementa `IQrRenderer`. `RenderPng` genera un PNG cuadrado con el QR (ECC H) que codifica `BuildUrl(...)`, el código impreso centrado debajo, y si `LogoPng` no es null, el logo centrado sobre el QR con un recuadro blanco de respaldo.
  - Registro DI: `services.AddSingleton<IQrRenderer, SkiaQrRenderer>();` + `QrOptions` bindeado desde configuración (sección `Qr`).

**Nota:** el test de aceptación **decodifica** el PNG generado con ZXing y afirma que el texto decodificado es exactamente la URL esperada — incluso con el logo encima (por eso ECC H). Este es el criterio de verdad; ajustar detalles de API de SkiaSharp/QRCoder a la versión instalada si difieren, manteniendo este comportamiento.

- [ ] **Step 1: Agregar paquetes**

```bash
dotnet add src/Valentinos.Infrastructure package QRCoder
dotnet add src/Valentinos.Infrastructure package SkiaSharp
dotnet add src/Valentinos.Infrastructure package SkiaSharp.NativeAssets.Linux   # no-op en Windows; asegura render headless multiplataforma
dotnet add tests/Valentinos.Tests package ZXing.Net.Bindings.SkiaSharp
```

> Si `SkiaSharp.NativeAssets.Linux` causa conflictos en el entorno actual (Windows), puede omitirse; SkiaSharp trae los assets nativos de Windows por default. Documentar la decisión.

- [ ] **Step 2: Escribir el test que falla**

`tests/Valentinos.Tests/Qr/SkiaQrRendererTests.cs`:

```csharp
using SkiaSharp;
using Valentinos.Application.Qr;
using Valentinos.Infrastructure.Qr;
using Xunit;
using ZXing.SkiaSharp;

namespace Valentinos.Tests.Qr;

public class SkiaQrRendererTests
{
    private static SkiaQrRenderer Build(string baseUrl = "https://app.valentinos.com")
        => new(new QrOptions { BaseUrl = baseUrl });

    [Fact]
    public void BuildUrl_ComponeLaUrlPublicaDeReporte()
    {
        var r = Build("https://app.valentinos.com");
        Assert.Equal("https://app.valentinos.com/r/mastercorp/VAC-001",
            r.BuildUrl("mastercorp", "VAC-001"));
    }

    [Fact]
    public void RenderPng_ProduceQrDecodificableConLaUrl()
    {
        var r = Build();
        var png = r.RenderPng(new QrRenderRequest("mastercorp", "VAC-001", LogoPng: null));

        Assert.NotNull(png);
        Assert.True(png.Length > 0);

        using var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);

        var reader = new BarcodeReader();
        var result = reader.Decode(bmp);
        Assert.NotNull(result);
        Assert.Equal("https://app.valentinos.com/r/mastercorp/VAC-001", result.Text);
    }

    [Fact]
    public void RenderPng_ConLogo_SigueSiendoDecodificable()
    {
        var r = Build();
        var logo = MakeSolidPng(80, 80, SKColors.RoyalBlue);
        var png = r.RenderPng(new QrRenderRequest("mastercorp", "VAC-007", logo));

        using var bmp = SKBitmap.Decode(png);
        var reader = new BarcodeReader();
        var result = reader.Decode(bmp);
        Assert.NotNull(result);
        Assert.Equal("https://app.valentinos.com/r/mastercorp/VAC-007", result.Text);
    }

    private static byte[] MakeSolidPng(int w, int h, SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        surface.Canvas.Clear(color);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
```

- [ ] **Step 3: Verificar que el test falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SkiaQrRendererTests`
Expected: FAIL de compilación — `IQrRenderer`/`SkiaQrRenderer` no existen.

- [ ] **Step 4: Definir opciones e interfaz en Application**

`src/Valentinos.Application/Qr/QrOptions.cs`:

```csharp
namespace Valentinos.Application.Qr;

public class QrOptions
{
    public string BaseUrl { get; set; } = "https://app.valentinos.com";
}
```

`src/Valentinos.Application/Qr/IQrRenderer.cs`:

```csharp
namespace Valentinos.Application.Qr;

public record QrRenderRequest(string Slug, string Codigo, byte[]? LogoPng);

public interface IQrRenderer
{
    byte[] RenderPng(QrRenderRequest req);
    string BuildUrl(string slug, string codigo);
}
```

- [ ] **Step 5: Implementar SkiaQrRenderer**

`src/Valentinos.Infrastructure/Qr/SkiaQrRenderer.cs`:

```csharp
using QRCoder;
using SkiaSharp;
using Valentinos.Application.Qr;

namespace Valentinos.Infrastructure.Qr;

public class SkiaQrRenderer : IQrRenderer
{
    private const int ModulePixels = 12;   // tamaño de cada módulo del QR en px
    private const int QuietModules = 4;     // margen "quiet zone" en módulos
    private const int LabelHeight = 64;     // franja inferior para el código impreso

    private readonly QrOptions _options;

    public SkiaQrRenderer(QrOptions options) => _options = options;

    public string BuildUrl(string slug, string codigo)
        => $"{_options.BaseUrl.TrimEnd('/')}/r/{slug}/{codigo}";

    public byte[] RenderPng(QrRenderRequest req)
    {
        var url = BuildUrl(req.Slug, req.Codigo);

        // 1) Matriz del QR con máxima corrección de errores (tolera el logo central).
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        var matrix = data.ModuleMatrix;              // List<BitArray>, incluye quiet zone
        var modules = matrix.Count;
        var side = modules * ModulePixels;

        var imageInfo = new SKImageInfo(side, side + LabelHeight);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // 2) Dibujar módulos negros.
        using (var black = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill })
        {
            for (var y = 0; y < modules; y++)
            {
                var row = matrix[y];
                for (var x = 0; x < modules; x++)
                {
                    if (row[x])
                        canvas.DrawRect(x * ModulePixels, y * ModulePixels, ModulePixels, ModulePixels, black);
                }
            }
        }

        // 3) Logo central opcional con recuadro blanco de respaldo (~22% del lado).
        if (req.LogoPng is { Length: > 0 })
        {
            using var logo = SKBitmap.Decode(req.LogoPng);
            if (logo is not null)
            {
                var logoSide = (int)(side * 0.22);
                var pad = logoSide / 6;
                var cx = side / 2f;
                var cy = side / 2f;
                var boxRect = new SKRect(cx - logoSide / 2f - pad, cy - logoSide / 2f - pad,
                                         cx + logoSide / 2f + pad, cy + logoSide / 2f + pad);
                using (var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
                    canvas.DrawRect(boxRect, white);

                var logoRect = new SKRect(cx - logoSide / 2f, cy - logoSide / 2f,
                                          cx + logoSide / 2f, cy + logoSide / 2f);
                canvas.DrawBitmap(logo, logoRect);
            }
        }

        // 4) Código impreso centrado en la franja inferior.
        using (var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var font = new SKFont(SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default, 34))
        {
            var baseline = side + (LabelHeight + 24) / 2f + 8;
            canvas.DrawText(req.Codigo, side / 2f, baseline, SKTextAlign.Center, font, textPaint);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
```

> Si la versión de SkiaSharp instalada expone la API de texto de forma distinta (p. ej. `SKPaint.TextSize`/`canvas.DrawText(text,x,y,paint)` en 2.x vs `SKFont` en 3.x), ajustar SOLO esa llamada para dibujar el código; el test no verifica el texto (verifica la decodificación del QR), así que basta con que compile y no rompa el QR.

- [ ] **Step 6: Registrar renderer + opciones en DI**

En `src/Valentinos.Infrastructure/DependencyInjection.cs`, agregar `using Valentinos.Application.Qr;`, `using Valentinos.Infrastructure.Qr;` y `using Microsoft.Extensions.Configuration;`. Cambiar la firma para recibir configuración y registrar:

```csharp
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, IConfiguration? configuration = null)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IAssetService, AssetService>();

        var qrOptions = new QrOptions();
        configuration?.GetSection("Qr").Bind(qrOptions);
        services.AddSingleton(qrOptions);
        services.AddSingleton<IQrRenderer, SkiaQrRenderer>();

        return services;
    }
```

Y en `src/Valentinos.Api/Program.cs`, actualizar la llamada para pasar la configuración:

```csharp
builder.Services.AddInfrastructure(connectionString, builder.Configuration);
```

> El parámetro `configuration` es opcional (default `null`) para no romper llamadas existentes; con `null`, `QrOptions` usa su `BaseUrl` por default.

- [ ] **Step 7: Ejecutar los tests del renderer para verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SkiaQrRendererTests`
Expected: PASS (3 tests) — incluida la decodificación con y sin logo.

- [ ] **Step 8: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (todo verde). Luego:

```bash
git add -A
git commit -m "feat: render de QR con SkiaSharp (código impreso y logo, verificado por decodificación)"
```

---

### Task 4: Hoja de QR en lote (PDF)

**Files:**
- Create: `src/Valentinos.Application/Qr/IQrSheetRenderer.cs`
- Create: `src/Valentinos.Infrastructure/Qr/SkiaQrSheetRenderer.cs`
- Modify: `src/Valentinos.Infrastructure/DependencyInjection.cs` (registrar)
- Test: `tests/Valentinos.Tests/Qr/SkiaQrSheetRendererTests.cs`

**Interfaces:**
- Consumes: `IQrRenderer` (Task 3).
- Produces:
  - `record QrSheetItem(string Slug, string Codigo, byte[]? LogoPng);`
  - `interface IQrSheetRenderer { byte[] RenderPdf(IReadOnlyList<QrSheetItem> items); }`
  - `SkiaQrSheetRenderer` implementa `IQrSheetRenderer` usando `IQrRenderer` para cada ítem y `SKDocument.CreatePdf` para componer una grilla (p. ej. 3 columnas) en páginas A4.
  - Registro DI: `services.AddSingleton<IQrSheetRenderer, SkiaQrSheetRenderer>();`

**Nota:** el test verifica que la salida es un **PDF válido** (empieza con `%PDF-`) y de tamaño no trivial para N ítems. No se decodifica el PDF (fuera de alcance); la corrección del QR ya la garantiza Task 3.

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Qr/SkiaQrSheetRendererTests.cs`:

```csharp
using System.Text;
using Valentinos.Application.Qr;
using Valentinos.Infrastructure.Qr;
using Xunit;

namespace Valentinos.Tests.Qr;

public class SkiaQrSheetRendererTests
{
    private static SkiaQrSheetRenderer Build()
        => new(new SkiaQrRenderer(new QrOptions { BaseUrl = "https://app.valentinos.com" }));

    [Fact]
    public void RenderPdf_VariosItems_ProduceUnPdfValido()
    {
        var sheet = Build();
        var items = new List<QrSheetItem>
        {
            new("mastercorp", "VAC-001", null),
            new("mastercorp", "VAC-002", null),
            new("mastercorp", "VAC-003", null),
            new("mastercorp", "VAC-004", null),
        };

        var pdf = sheet.RenderPdf(items);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000, "El PDF debería tener contenido no trivial.");
        var header = Encoding.ASCII.GetString(pdf, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void RenderPdf_ListaVacia_ProducePdfValido()
    {
        var sheet = Build();
        var pdf = sheet.RenderPdf(new List<QrSheetItem>());
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }
}
```

- [ ] **Step 2: Verificar que el test falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SkiaQrSheetRendererTests`
Expected: FAIL de compilación — tipos no existen.

- [ ] **Step 3: Definir la interfaz en Application**

`src/Valentinos.Application/Qr/IQrSheetRenderer.cs`:

```csharp
namespace Valentinos.Application.Qr;

public record QrSheetItem(string Slug, string Codigo, byte[]? LogoPng);

public interface IQrSheetRenderer
{
    byte[] RenderPdf(IReadOnlyList<QrSheetItem> items);
}
```

- [ ] **Step 4: Implementar SkiaQrSheetRenderer**

`src/Valentinos.Infrastructure/Qr/SkiaQrSheetRenderer.cs`:

```csharp
using SkiaSharp;
using Valentinos.Application.Qr;

namespace Valentinos.Infrastructure.Qr;

public class SkiaQrSheetRenderer : IQrSheetRenderer
{
    // A4 a 72 dpi aprox.
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 36f;
    private const int Columns = 3;
    private const int RowsPerPage = 4;

    private readonly IQrRenderer _qr;

    public SkiaQrSheetRenderer(IQrRenderer qr) => _qr = qr;

    public byte[] RenderPdf(IReadOnlyList<QrSheetItem> items)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            var perPage = Columns * RowsPerPage;
            var cellW = (PageWidth - 2 * Margin) / Columns;
            var cellH = (PageHeight - 2 * Margin) / RowsPerPage;

            // Siempre emitir al menos una página (aunque no haya ítems) para un PDF válido.
            var pages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)perPage));

            for (var page = 0; page < pages; page++)
            {
                var canvas = doc.BeginPage(PageWidth, PageHeight);
                for (var i = 0; i < perPage; i++)
                {
                    var index = page * perPage + i;
                    if (index >= items.Count) break;

                    var item = items[index];
                    var png = _qr.RenderPng(new QrRenderRequest(item.Slug, item.Codigo, item.LogoPng));
                    using var bmp = SKBitmap.Decode(png);
                    if (bmp is null) continue;

                    var col = i % Columns;
                    var rowIdx = i / Columns;
                    var x = Margin + col * cellW;
                    var y = Margin + rowIdx * cellH;

                    // Encajar el QR (cuadrado + franja) dentro de la celda, con padding.
                    var padding = 8f;
                    var maxW = cellW - 2 * padding;
                    var maxH = cellH - 2 * padding;
                    var scale = Math.Min(maxW / bmp.Width, maxH / bmp.Height);
                    var drawW = bmp.Width * scale;
                    var drawH = bmp.Height * scale;
                    var dx = x + (cellW - drawW) / 2f;
                    var dy = y + (cellH - drawH) / 2f;
                    canvas.DrawBitmap(bmp, new SKRect(dx, dy, dx + drawW, dy + drawH));
                }
                doc.EndPage();
            }
            doc.Close();
        }
        return ms.ToArray();
    }
}
```

- [ ] **Step 5: Registrar en DI**

En `src/Valentinos.Infrastructure/DependencyInjection.cs`, junto al registro de `IQrRenderer`:

```csharp
        services.AddSingleton<IQrSheetRenderer, SkiaQrSheetRenderer>();
```

- [ ] **Step 6: Ejecutar los tests para verificar que pasan**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SkiaQrSheetRendererTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (todo verde). Luego:

```bash
git add -A
git commit -m "feat: generación de hoja PDF con múltiples QR en grilla"
```

---

## Self-Review

**Spec coverage (Plan 2 scope):**
- AssetType/Asset con prefijo autogenerado → Task 1 (entidades) + Task 2 (correlativo atómico) ✓
- Códigos únicos por tenant → índices únicos (Task 1) + servicio (Task 2) ✓
- QR apuntando a `{BaseUrl}/r/{slug}/{codigo}` con código impreso y logo marca de agua → Task 3 ✓
- Descarga en lote (hoja PDF) → Task 4 ✓
- Aislamiento por tenant → heredado del Plan 1 (entidades `ITenantOwned`, filtro global) — verificado indirectamente porque los servicios usan `AppDbContext` con `ITenantContext`.
- **Fuera de alcance de Plan 2 (movido a Plan 4 con auth):** endpoints HTTP de administración (`GET /api/assets/{id}/qr`, `POST /api/assets/qr-batch`, CRUD REST). Motivo: requieren JWT admin; exponerlos sin auth sería un agujero de seguridad. Se conectan en el Plan 4 llamando a `IAssetService`/`IQrRenderer`/`IQrSheetRenderer`.

**Placeholder scan:** sin TBD/TODO; el código de cada paso está completo. La única flexibilidad explícita es la adaptación de firmas de APIs de terceros (QRCoder/SkiaSharp/ZXing) a la versión instalada, con el test de decodificación/validez como criterio de aceptación.

**Type consistency:** `IAssetService` con las mismas firmas donde se consume; `IQrRenderer.RenderPng(QrRenderRequest)`/`BuildUrl(slug,codigo)` usados igual en Task 3 y Task 4; `QrRenderRequest`/`QrSheetItem` con los mismos campos `(Slug, Codigo, LogoPng)`. `AddInfrastructure` gana un parámetro opcional `IConfiguration?` sin romper el llamado del Plan 1 (se actualiza la llamada en `Program.cs`).

**Notas de entorno:** SkiaSharp requiere assets nativos; en Windows vienen por default. Si el entorno de tests fuera headless Linux, `SkiaSharp.NativeAssets.Linux` cubre el render. La migración de Task 1 no se aplica a la BD (solo se genera y verifica).
