# Plan 3 — Flujo de Reporte Público Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que un housekeeper, sin autenticarse, escanee el QR de un activo y envíe un reporte de avería (descripción, severidad, piso, fotos) desde un endpoint público; guardar las fotos vía una abstracción de almacenamiento; y disparar una **alerta por email** a los admins del tenant al crearse el reporte, mediante una abstracción de canales de notificación (email activo; WhatsApp/SMS/In-App diseñados y apagados por default).

**Architecture:** Se construye sobre Planes 1-2. Entidades `Report` y `ReportPhoto` (ambas `ITenantOwned`) en Domain. `IFileStorage` (Application) con implementación de disco local `LocalFileStorage` (Infrastructure). `IReportService` (Application) crea el reporte: resuelve el `Asset` por código dentro del tenant, guarda fotos y persiste `Report` + `ReportPhoto`. La **notificación** usa `INotificationChannel` (patrón strategy): `EmailChannel` activo (envía vía `IEmailSender`), más stubs `InAppChannel`/`WhatsAppChannel`/`SmsChannel` **apagados por config**. `NotificationService` despacha a los canales habilitados por tenant. El tenant expone su config de notificaciones (columnas nuevas). El endpoint **público anónimo** `POST /api/public/{slug}/reports` (multipart) crea el reporte y dispara la alerta; el tenant se resuelve por slug (middleware del Plan 1). Se agrega **rate limiting** al endpoint público.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server / InMemory en tests), ASP.NET Core rate limiting (`Microsoft.AspNetCore.RateLimiting`, incluido en el framework), `System.Net.Mail` para SMTP, xUnit + WebApplicationFactory.

## Global Constraints

- Target framework: **net10.0**; namespaces raíz `Valentinos.*`.
- `Report` y `ReportPhoto` implementan `ITenantOwned` (`Guid TenantId`) y heredan de `BaseEntity`. El aislamiento por tenant y el estampado/fail-closed lo aplica `AppDbContext` (Plan 1) — no duplicar.
- Enums: `Severidad { Leve, AMedias, NoFunciona }`; `ReportStatus { Nuevo, EnReparacion, Resuelto }`. Un reporte nuevo nace en `ReportStatus.Nuevo`; las transiciones de estado son del Plan 4 (aquí solo se crea).
- La creación de un reporte **debe tener éxito aunque falle la notificación** (email best-effort): el envío se hace tras persistir y sus errores se capturan/loguean, nunca propagan al housekeeper.
- Solo **EmailChannel** está activo por default (`Tenant.EmailNotificationsEnabled = true`). WhatsApp/SMS/In-App: implementados como stubs, `Enabled = false` por default, encendibles desde la config del tenant sin cambios de código.
- El endpoint `POST /api/public/{slug}/reports` es **anónimo** (sin JWT) pero con **rate limiting** (fixed window) para mitigar abuso. No expone datos de otros tenants.
- Fotos: se guardan vía `IFileStorage` (disco local en MVP, raíz configurable). `ReportPhoto` guarda `FileKey` (clave opaca) y `ContentType`. Máximo configurable de fotos por reporte (default 5) y tamaño por foto (validado en el endpoint).
- Migraciones EF: seguir las reglas de seguridad del agente `sr-net-developer` (nunca `--no-build`; verificar el `.cs`; no aplicar a la BD sin autorización). El snapshot es `AppDbContextModelSnapshot.cs`.
- Lógica de negocio con **TDD**. Los envíos de email en tests usan un `IEmailSender` falso (nunca SMTP real). Commits en español, imperativo, prefijo convencional.

---

### Task 1: Entidades Report y ReportPhoto + migración

**Files:**
- Create: `src/Valentinos.Domain/Enums/Severidad.cs`
- Create: `src/Valentinos.Domain/Enums/ReportStatus.cs`
- Create: `src/Valentinos.Domain/Entities/Report.cs`
- Create: `src/Valentinos.Domain/Entities/ReportPhoto.cs`
- Modify: `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs` (DbSets + config)
- Create: migración `AddReportAndReportPhoto`
- Test: `tests/Valentinos.Tests/Domain/ReportTests.cs`

**Interfaces:**
- Consumes: `BaseEntity`, `ITenantOwned`, `Asset`.
- Produces:
  - `enum Severidad { Leve, AMedias, NoFunciona }`
  - `enum ReportStatus { Nuevo, EnReparacion, Resuelto }`
  - `class Report : BaseEntity, ITenantOwned { Guid TenantId; Guid AssetId; string Descripcion; Severidad Severidad; string? Ubicacion; string? ReportadoPor; ReportStatus Estado; string? Notas; DateTime? ResolvedAt; }` con `static Report Create(Guid assetId, string descripcion, Severidad severidad, string? ubicacion, string? reportadoPor)` que valida descripción no vacía y nace en `ReportStatus.Nuevo`.
  - `class ReportPhoto : BaseEntity, ITenantOwned { Guid TenantId; Guid ReportId; string FileKey; string ContentType; }`
  - `AppDbContext.Reports`, `AppDbContext.ReportPhotos` (DbSets) + índices/relaciones.

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Domain/ReportTests.cs`:

```csharp
using Valentinos.Domain.Entities;
using Valentinos.Domain.Enums;
using Xunit;

namespace Valentinos.Tests.Domain;

public class ReportTests
{
    [Fact]
    public void Create_ReporteNuevo_TieneEstadoNuevoYCampos()
    {
        var assetId = Guid.NewGuid();
        var r = Report.Create(assetId, "No enciende", Severidad.NoFunciona, "Piso 3", "Ana");

        Assert.Equal(assetId, r.AssetId);
        Assert.Equal("No enciende", r.Descripcion);
        Assert.Equal(Severidad.NoFunciona, r.Severidad);
        Assert.Equal("Piso 3", r.Ubicacion);
        Assert.Equal("Ana", r.ReportadoPor);
        Assert.Equal(ReportStatus.Nuevo, r.Estado);
        Assert.Null(r.ResolvedAt);
    }

    [Fact]
    public void Create_DescripcionVacia_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(
            () => Report.Create(Guid.NewGuid(), "   ", Severidad.Leve, null, null));
    }
}
```

- [ ] **Step 2: Ejecutar el test para verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~ReportTests`
Expected: FAIL de compilación — `Report` no existe.

- [ ] **Step 3: Implementar enums y entidades**

`src/Valentinos.Domain/Enums/Severidad.cs`:

```csharp
namespace Valentinos.Domain.Enums;

public enum Severidad
{
    Leve = 0,
    AMedias = 1,
    NoFunciona = 2
}
```

`src/Valentinos.Domain/Enums/ReportStatus.cs`:

```csharp
namespace Valentinos.Domain.Enums;

public enum ReportStatus
{
    Nuevo = 0,
    EnReparacion = 1,
    Resuelto = 2
}
```

`src/Valentinos.Domain/Entities/Report.cs`:

```csharp
using Valentinos.Domain.Common;
using Valentinos.Domain.Enums;

namespace Valentinos.Domain.Entities;

public class Report : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AssetId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public Severidad Severidad { get; set; }
    public string? Ubicacion { get; set; }
    public string? ReportadoPor { get; set; }
    public ReportStatus Estado { get; set; } = ReportStatus.Nuevo;
    public string? Notas { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public static Report Create(Guid assetId, string descripcion, Severidad severidad,
        string? ubicacion, string? reportadoPor)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new ArgumentException("La descripción no puede estar vacía.", nameof(descripcion));

        return new Report
        {
            AssetId = assetId,
            Descripcion = descripcion.Trim(),
            Severidad = severidad,
            Ubicacion = ubicacion,
            ReportadoPor = reportadoPor,
            Estado = ReportStatus.Nuevo
        };
    }
}
```

`src/Valentinos.Domain/Entities/ReportPhoto.cs`:

```csharp
using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class ReportPhoto : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    public string FileKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}
```

- [ ] **Step 4: DbSets y configuración en AppDbContext**

En `src/Valentinos.Infrastructure/Persistence/AppDbContext.cs`, junto a los DbSets existentes:

```csharp
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportPhoto> ReportPhotos => Set<ReportPhoto>();
```

Y en `OnModelCreating`, después del bloque `modelBuilder.Entity<Asset>(...)` y **antes** del `foreach` del global query filter:

```csharp
        modelBuilder.Entity<Report>(e =>
        {
            e.Property(r => r.Descripcion).HasMaxLength(2000).IsRequired();
            e.Property(r => r.Ubicacion).HasMaxLength(200);
            e.Property(r => r.ReportadoPor).HasMaxLength(200);
            e.Property(r => r.Notas).HasMaxLength(2000);
            e.HasIndex(r => new { r.TenantId, r.AssetId });
            e.HasOne<Asset>()
             .WithMany()
             .HasForeignKey(r => r.AssetId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReportPhoto>(e =>
        {
            e.Property(p => p.FileKey).HasMaxLength(400).IsRequired();
            e.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
            e.HasIndex(p => p.ReportId);
            e.HasOne<Report>()
             .WithMany()
             .HasForeignKey(p => p.ReportId)
             .OnDelete(DeleteBehavior.Cascade);
        });
```

> `Report` y `ReportPhoto` implementan `ITenantOwned`; el `foreach` existente les aplica el filtro global automáticamente. No agregar filtros manuales.

- [ ] **Step 5: Ejecutar el test de dominio (pasa)**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~ReportTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Generar la migración**

```bash
dotnet ef migrations add AddReportAndReportPhoto \
  --project src/Valentinos.Infrastructure \
  --startup-project src/Valentinos.Api \
  --output-dir Persistence/Migrations
```

Abrir el `.cs` generado y verificar: tablas `Reports` y `ReportPhotos`; FK `Reports.AssetId → Assets.Id` (Restrict) y `ReportPhotos.ReportId → Reports.Id` (Cascade); índices `IX_Reports_TenantId_AssetId` e `IX_ReportPhotos_ReportId`; **sin shadow FKs** (`AssetId1`, `ReportId1`). No aplicar a la BD.

- [ ] **Step 7: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (verde). Luego:

```bash
git add -A
git commit -m "feat: entidades Report y ReportPhoto con migración"
```

---

### Task 2: Abstracción de almacenamiento de archivos (IFileStorage)

**Files:**
- Create: `src/Valentinos.Application/Storage/IFileStorage.cs`
- Create: `src/Valentinos.Application/Storage/FileStorageOptions.cs`
- Create: `src/Valentinos.Infrastructure/Storage/LocalFileStorage.cs`
- Modify: `src/Valentinos.Infrastructure/DependencyInjection.cs`
- Test: `tests/Valentinos.Tests/Storage/LocalFileStorageTests.cs`

**Interfaces:**
- Produces:
  - `class FileStorageOptions { string RootPath { get; set; } = "storage"; }`
  - `interface IFileStorage { Task<string> SaveAsync(byte[] content, string extension, string prefix, CancellationToken ct = default); Task<byte[]?> GetAsync(string fileKey, CancellationToken ct = default); }`
  - `LocalFileStorage` implementa `IFileStorage`; guarda en `{RootPath}/{prefix}/{guid}.{extension}` y devuelve el `FileKey` relativo (`{prefix}/{guid}.{extension}`). `GetAsync` lee el archivo por `FileKey`; devuelve `null` si no existe. Nunca permite path traversal (rechaza `fileKey` con `..`).
  - Registro DI: `FileStorageOptions` (singleton bindeado de config `FileStorage`) + `services.AddSingleton<IFileStorage, LocalFileStorage>()`.

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Storage/LocalFileStorageTests.cs`:

```csharp
using System.Text;
using Valentinos.Application.Storage;
using Valentinos.Infrastructure.Storage;
using Xunit;

namespace Valentinos.Tests.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "valentinos-fs-" + Guid.NewGuid());

    private LocalFileStorage Build() => new(new FileStorageOptions { RootPath = _root });

    [Fact]
    public async Task SaveAsync_LuegoGetAsync_DevuelveElMismoContenido()
    {
        var fs = Build();
        var bytes = Encoding.UTF8.GetBytes("hola");

        var key = await fs.SaveAsync(bytes, "jpg", "mastercorp");

        Assert.StartsWith("mastercorp/", key.Replace('\\', '/'));
        Assert.EndsWith(".jpg", key);

        var back = await fs.GetAsync(key);
        Assert.NotNull(back);
        Assert.Equal(bytes, back);
    }

    [Fact]
    public async Task GetAsync_ClaveInexistente_DevuelveNull()
    {
        var fs = Build();
        Assert.Null(await fs.GetAsync("mastercorp/no-existe.jpg"));
    }

    [Fact]
    public async Task GetAsync_ConPathTraversal_LanzaExcepcion()
    {
        var fs = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => fs.GetAsync("../secreto.txt"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~LocalFileStorageTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Definir opciones e interfaz en Application**

`src/Valentinos.Application/Storage/FileStorageOptions.cs`:

```csharp
namespace Valentinos.Application.Storage;

public class FileStorageOptions
{
    public string RootPath { get; set; } = "storage";
}
```

`src/Valentinos.Application/Storage/IFileStorage.cs`:

```csharp
namespace Valentinos.Application.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(byte[] content, string extension, string prefix, CancellationToken ct = default);
    Task<byte[]?> GetAsync(string fileKey, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar LocalFileStorage**

`src/Valentinos.Infrastructure/Storage/LocalFileStorage.cs`:

```csharp
using Valentinos.Application.Storage;

namespace Valentinos.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(FileStorageOptions options)
    {
        _root = Path.GetFullPath(options.RootPath);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, string prefix, CancellationToken ct = default)
    {
        var safePrefix = SanitizeSegment(prefix);
        var ext = extension.TrimStart('.');
        var fileName = $"{Guid.NewGuid():N}.{ext}";
        var relative = $"{safePrefix}/{fileName}";

        var absoluteDir = Path.Combine(_root, safePrefix);
        Directory.CreateDirectory(absoluteDir);
        var absolutePath = Path.Combine(absoluteDir, fileName);
        await File.WriteAllBytesAsync(absolutePath, content, ct);

        return relative;
    }

    public async Task<byte[]?> GetAsync(string fileKey, CancellationToken ct = default)
    {
        var absolutePath = ResolveWithinRoot(fileKey);
        if (!File.Exists(absolutePath)) return null;
        return await File.ReadAllBytesAsync(absolutePath, ct);
    }

    // Evita path traversal: la ruta resuelta debe quedar bajo _root.
    private string ResolveWithinRoot(string fileKey)
    {
        if (string.IsNullOrWhiteSpace(fileKey) || fileKey.Contains(".."))
            throw new ArgumentException("FileKey inválido.", nameof(fileKey));

        var combined = Path.GetFullPath(Path.Combine(_root, fileKey));
        if (!combined.StartsWith(_root, StringComparison.Ordinal))
            throw new ArgumentException("FileKey fuera del almacenamiento.", nameof(fileKey));
        return combined;
    }

    private static string SanitizeSegment(string segment)
    {
        var s = (segment ?? string.Empty).Trim().Replace("\\", "-").Replace("/", "-");
        return string.IsNullOrWhiteSpace(s) ? "general" : s;
    }
}
```

- [ ] **Step 5: Registrar en DI**

En `src/Valentinos.Infrastructure/DependencyInjection.cs`, agregar `using Valentinos.Application.Storage;` y `using Valentinos.Infrastructure.Storage;`, y dentro de `AddInfrastructure` antes de `return services;`:

```csharp
        var fileStorageOptions = new FileStorageOptions();
        configuration?.GetSection("FileStorage").Bind(fileStorageOptions);
        services.AddSingleton(fileStorageOptions);
        services.AddSingleton<IFileStorage, LocalFileStorage>();
```

- [ ] **Step 6: Tests del storage (pasan)**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~LocalFileStorageTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (verde). Luego:

```bash
git add -A
git commit -m "feat: abstracción IFileStorage con implementación de disco local"
```

---

### Task 3: Servicio de creación de reportes

**Files:**
- Create: `src/Valentinos.Application/Reports/IReportService.cs`
- Create: `src/Valentinos.Application/Reports/ReportDtos.cs`
- Create: `src/Valentinos.Infrastructure/Reports/ReportService.cs`
- Modify: `src/Valentinos.Infrastructure/DependencyInjection.cs`
- Test: `tests/Valentinos.Tests/Reports/ReportServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `ITenantContext`, `IFileStorage`, `Asset`, `Report`, `ReportPhoto`, `Severidad`.
- Produces:
  - `record ReportPhotoInput(byte[] Content, string ContentType);`
  - `record CreateReportRequest(string AssetCodigo, string Descripcion, Severidad Severidad, string? Ubicacion, string? ReportadoPor, IReadOnlyList<ReportPhotoInput> Photos);`
  - `record ReportDto(Guid Id, Guid AssetId, string AssetCodigo, string Descripcion, string Severidad, string? Ubicacion, string? ReportadoPor, string Estado, int PhotoCount, DateTime CreatedAt);`
  - `interface IReportService { Task<ReportDto> CreateReportAsync(CreateReportRequest req, CancellationToken ct = default); }`
  - `ReportService.ExtensionForContentType(string contentType)` (público estático) → `jpg`/`png`/`bin`.
  - Registro DI: `services.AddScoped<IReportService, ReportService>();`

**Comportamiento:** resuelve el `Asset` por `Codigo` (query filter → solo del tenant actual); si no existe lanza `InvalidOperationException`. Guarda cada foto vía `IFileStorage` con `prefix` = slug/tenant (usa el `TenantId` como prefix para simplicidad) y crea `ReportPhoto` por cada una. Persiste `Report` + fotos en un `SaveChanges`. (La notificación se agrega en la Task 4; aquí el servicio aún no notifica.)

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Reports/ReportServiceTests.cs`:

```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Assets;
using Valentinos.Application.Reports;
using Valentinos.Application.Storage;
using Valentinos.Domain.Enums;
using Valentinos.Infrastructure.Assets;
using Valentinos.Infrastructure.Persistence;
using Valentinos.Infrastructure.Reports;
using Xunit;

namespace Valentinos.Tests.Reports;

public class ReportServiceTests
{
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    // Almacenamiento en memoria para tests (no toca disco).
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public Task<string> SaveAsync(byte[] content, string extension, string prefix, CancellationToken ct = default)
        {
            var key = $"{prefix}/{Guid.NewGuid():N}.{extension}";
            Files[key] = content;
            return Task.FromResult(key);
        }
        public Task<byte[]?> GetAsync(string fileKey, CancellationToken ct = default)
            => Task.FromResult(Files.TryGetValue(fileKey, out var b) ? b : null);
    }

    private static (AppDbContext db, IAssetService assets, IReportService reports, InMemoryFileStorage fs)
        Build(Guid tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ctx = new FixedTenantContext();
        ctx.Set(tenant);
        var db = new AppDbContext(options, ctx);
        var fs = new InMemoryFileStorage();
        var assets = new AssetService(db, ctx);
        var reports = new ReportService(db, ctx, fs);
        return (db, assets, reports, fs);
    }

    [Fact]
    public async Task CreateReportAsync_CreaReporteConFotos()
    {
        var (_, assets, reports, fs) = Build(Guid.NewGuid());
        var tipo = await assets.CreateAssetTypeAsync(new CreateAssetTypeRequest("Aspiradora", "VAC"));
        var asset = await assets.CreateAssetAsync(new CreateAssetRequest(tipo.Id, "Piso 1"));

        var foto = new ReportPhotoInput(Encoding.UTF8.GetBytes("img"), "image/jpeg");
        var dto = await reports.CreateReportAsync(new CreateReportRequest(
            asset.Codigo, "No aspira", Severidad.NoFunciona, "Piso 1", "Ana",
            new List<ReportPhotoInput> { foto }));

        Assert.Equal(asset.Codigo, dto.AssetCodigo);
        Assert.Equal("Nuevo", dto.Estado);
        Assert.Equal("NoFunciona", dto.Severidad);
        Assert.Equal(1, dto.PhotoCount);
        Assert.Single(fs.Files);
    }

    [Fact]
    public async Task CreateReportAsync_SinFotos_Funciona()
    {
        var (_, assets, reports, _) = Build(Guid.NewGuid());
        var tipo = await assets.CreateAssetTypeAsync(new CreateAssetTypeRequest("Aspiradora", "VAC"));
        var asset = await assets.CreateAssetAsync(new CreateAssetRequest(tipo.Id, null));

        var dto = await reports.CreateReportAsync(new CreateReportRequest(
            asset.Codigo, "Ruido raro", Severidad.Leve, null, null,
            new List<ReportPhotoInput>()));

        Assert.Equal(0, dto.PhotoCount);
    }

    [Fact]
    public async Task CreateReportAsync_CodigoInexistente_LanzaExcepcion()
    {
        var (_, _, reports, _) = Build(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reports.CreateReportAsync(new CreateReportRequest(
                "VAC-999", "x", Severidad.Leve, null, null, new List<ReportPhotoInput>())));
    }
}
```

- [ ] **Step 2: Verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~ReportServiceTests`
Expected: FAIL de compilación.

- [ ] **Step 3: DTOs e interfaz en Application**

`src/Valentinos.Application/Reports/ReportDtos.cs`:

```csharp
using Valentinos.Domain.Enums;

namespace Valentinos.Application.Reports;

public record ReportPhotoInput(byte[] Content, string ContentType);

public record CreateReportRequest(
    string AssetCodigo,
    string Descripcion,
    Severidad Severidad,
    string? Ubicacion,
    string? ReportadoPor,
    IReadOnlyList<ReportPhotoInput> Photos);

public record ReportDto(
    Guid Id,
    Guid AssetId,
    string AssetCodigo,
    string Descripcion,
    string Severidad,
    string? Ubicacion,
    string? ReportadoPor,
    string Estado,
    int PhotoCount,
    DateTime CreatedAt);
```

`src/Valentinos.Application/Reports/IReportService.cs`:

```csharp
namespace Valentinos.Application.Reports;

public interface IReportService
{
    Task<ReportDto> CreateReportAsync(CreateReportRequest req, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar ReportService**

`src/Valentinos.Infrastructure/Reports/ReportService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Reports;
using Valentinos.Application.Storage;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;

    public ReportService(AppDbContext db, ITenantContext tenant, IFileStorage storage)
    {
        _db = db;
        _tenant = tenant;
        _storage = storage;
    }

    public static string ExtensionForContentType(string contentType) => contentType switch
    {
        "image/jpeg" or "image/jpg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "bin"
    };

    public async Task<ReportDto> CreateReportAsync(CreateReportRequest req, CancellationToken ct = default)
    {
        // El query filter garantiza que solo se resuelve un activo del tenant actual.
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Codigo == req.AssetCodigo, ct)
            ?? throw new InvalidOperationException("El activo no existe para este tenant.");

        var report = Report.Create(asset.Id, req.Descripcion, req.Severidad, req.Ubicacion, req.ReportadoPor);
        _db.Reports.Add(report);

        var prefix = _tenant.TenantId?.ToString("N") ?? "general";
        foreach (var photo in req.Photos)
        {
            var ext = ExtensionForContentType(photo.ContentType);
            var key = await _storage.SaveAsync(photo.Content, ext, prefix, ct);
            _db.ReportPhotos.Add(new ReportPhoto
            {
                ReportId = report.Id,
                FileKey = key,
                ContentType = photo.ContentType
            });
        }

        await _db.SaveChangesAsync(ct);

        return new ReportDto(
            report.Id, asset.Id, asset.Codigo, report.Descripcion,
            report.Severidad.ToString(), report.Ubicacion, report.ReportadoPor,
            report.Estado.ToString(), req.Photos.Count, report.CreatedAt);
    }
}
```

- [ ] **Step 5: Registrar en DI**

En `src/Valentinos.Infrastructure/DependencyInjection.cs`, agregar `using Valentinos.Application.Reports;`, `using Valentinos.Infrastructure.Reports;`, y antes de `return services;`:

```csharp
        services.AddScoped<IReportService, ReportService>();
```

- [ ] **Step 6: Tests del servicio (pasan)**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~ReportServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (verde). Luego:

```bash
git add -A
git commit -m "feat: servicio de creación de reportes con fotos vía IFileStorage"
```

---

### Task 4: Notificaciones (canales + email activo) y disparo al crear reporte

**Files:**
- Modify: `src/Valentinos.Domain/Entities/Tenant.cs` (columnas de config de notificación)
- Create: `src/Valentinos.Application/Notifications/IEmailSender.cs`
- Create: `src/Valentinos.Application/Notifications/INotificationChannel.cs`
- Create: `src/Valentinos.Application/Notifications/INotificationService.cs`
- Create: `src/Valentinos.Application/Notifications/ReportCreatedNotification.cs`
- Create: `src/Valentinos.Infrastructure/Notifications/EmailChannel.cs`
- Create: `src/Valentinos.Infrastructure/Notifications/DisabledChannels.cs` (InApp/WhatsApp/SMS stubs)
- Create: `src/Valentinos.Infrastructure/Notifications/NotificationService.cs`
- Create: `src/Valentinos.Infrastructure/Notifications/SmtpEmailSender.cs`
- Create: `src/Valentinos.Application/Notifications/SmtpOptions.cs`
- Modify: `src/Valentinos.Infrastructure/Reports/ReportService.cs` (disparar notificación tras crear)
- Modify: `src/Valentinos.Infrastructure/DependencyInjection.cs`
- Create: migración `AddNotificationConfigToTenant`
- Test: `tests/Valentinos.Tests/Notifications/NotificationServiceTests.cs`
- Modify: `tests/Valentinos.Tests/Reports/ReportServiceTests.cs` (constructor de `ReportService` gana un parámetro)

**Interfaces:**
- Produces:
  - `Tenant` gana: `bool EmailNotificationsEnabled = true; string? NotificationEmails; bool WhatsAppNotificationsEnabled = false; bool SmsNotificationsEnabled = false; bool InAppNotificationsEnabled = false;`
  - `record ReportCreatedNotification(Guid TenantId, string AssetCodigo, string Severidad, string Descripcion, string? Ubicacion, string? ReportadoPor);`
  - `interface IEmailSender { Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken ct = default); }`
  - `interface INotificationChannel { string Name { get; } bool IsEnabled(Tenant tenant); Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default); }`
  - `interface INotificationService { Task NotifyReportCreatedAsync(ReportCreatedNotification n, CancellationToken ct = default); }`
  - `EmailChannel` (usa `IEmailSender`; `IsEnabled` = `tenant.EmailNotificationsEnabled && emails no vacíos`), `InAppChannel`/`WhatsAppChannel`/`SmsChannel` (stubs; `IsEnabled` según flag del tenant, `false` default; `SendReportCreatedAsync` no-op).
  - `NotificationService` recibe `IEnumerable<INotificationChannel>` + `AppDbContext`; carga el `Tenant` por `n.TenantId` y despacha a los canales habilitados; **captura y traga errores por canal** (best-effort).
  - `SmtpEmailSender` implementa `IEmailSender` con `System.Net.Mail`; si `SmtpOptions.Enabled == false`, es no-op.
  - Registro DI de todo.

**Nota:** `ReportService` gana una dependencia `INotificationService` y, tras `SaveChanges`, llama `NotifyReportCreatedAsync(...)` dentro de un `try/catch` que traga errores (la creación no debe fallar por la notificación). El test de `ReportServiceTests` que construye `ReportService` a mano debe pasar un `INotificationService` falso (no-op).

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Notifications/NotificationServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Notifications;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Notifications;

public class NotificationServiceTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public int Calls;
        public List<string> LastTo = new();
        public Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken ct = default)
        {
            Calls++;
            LastTo = to.ToList();
            return Task.CompletedTask;
        }
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options, new NoTenantContext());
    }

    [Fact]
    public async Task NotifyReportCreated_EmailHabilitado_EnviaEmail()
    {
        var db = NewDb();
        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.EmailNotificationsEnabled = true;
        tenant.NotificationEmails = "admin@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        var sender = new RecordingEmailSender();
        var svc = new NotificationService(
            new INotificationChannel[] { new EmailChannel(sender) }, db);

        await svc.NotifyReportCreatedAsync(new ReportCreatedNotification(
            tenant.Id, "VAC-001", "NoFunciona", "No aspira", "Piso 1", "Ana"));

        Assert.Equal(1, sender.Calls);
        Assert.Contains("admin@mastercorp.com", sender.LastTo);
    }

    [Fact]
    public async Task NotifyReportCreated_EmailDeshabilitado_NoEnvia()
    {
        var db = NewDb();
        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.EmailNotificationsEnabled = false;
        tenant.NotificationEmails = "admin@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        var sender = new RecordingEmailSender();
        var svc = new NotificationService(
            new INotificationChannel[] { new EmailChannel(sender) }, db);

        await svc.NotifyReportCreatedAsync(new ReportCreatedNotification(
            tenant.Id, "VAC-001", "Leve", "x", null, null));

        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task NotifyReportCreated_CanalQueFalla_NoPropagaExcepcion()
    {
        var db = NewDb();
        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.EmailNotificationsEnabled = true;
        tenant.NotificationEmails = "admin@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        var svc = new NotificationService(
            new INotificationChannel[] { new ThrowingChannel() }, db);

        // No debe lanzar aunque el canal explote.
        await svc.NotifyReportCreatedAsync(new ReportCreatedNotification(
            tenant.Id, "VAC-001", "Leve", "x", null, null));
    }

    private sealed class ThrowingChannel : INotificationChannel
    {
        public string Name => "throwing";
        public bool IsEnabled(Tenant tenant) => true;
        public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }
}
```

- [ ] **Step 2: Verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~NotificationServiceTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Ampliar Tenant con la config de notificaciones**

En `src/Valentinos.Domain/Entities/Tenant.cs`, agregar propiedades (después de `LogoUrl`):

```csharp
    public bool EmailNotificationsEnabled { get; set; } = true;
    public string? NotificationEmails { get; set; }
    public bool WhatsAppNotificationsEnabled { get; set; }
    public bool SmsNotificationsEnabled { get; set; }
    public bool InAppNotificationsEnabled { get; set; }
```

- [ ] **Step 4: Definir contratos en Application**

`src/Valentinos.Application/Notifications/SmtpOptions.cs`:

```csharp
namespace Valentinos.Application.Notifications;

public class SmtpOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "no-reply@valentinos.com";
}
```

`src/Valentinos.Application/Notifications/IEmailSender.cs`:

```csharp
namespace Valentinos.Application.Notifications;

public interface IEmailSender
{
    Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken ct = default);
}
```

`src/Valentinos.Application/Notifications/ReportCreatedNotification.cs`:

```csharp
namespace Valentinos.Application.Notifications;

public record ReportCreatedNotification(
    Guid TenantId,
    string AssetCodigo,
    string Severidad,
    string Descripcion,
    string? Ubicacion,
    string? ReportadoPor);
```

`src/Valentinos.Application/Notifications/INotificationChannel.cs`:

```csharp
using Valentinos.Domain.Entities;

namespace Valentinos.Application.Notifications;

public interface INotificationChannel
{
    string Name { get; }
    bool IsEnabled(Tenant tenant);
    Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default);
}
```

`src/Valentinos.Application/Notifications/INotificationService.cs`:

```csharp
namespace Valentinos.Application.Notifications;

public interface INotificationService
{
    Task NotifyReportCreatedAsync(ReportCreatedNotification n, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implementar canales, servicio y sender en Infrastructure**

`src/Valentinos.Infrastructure/Notifications/EmailChannel.cs`:

```csharp
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Notifications;

public class EmailChannel : INotificationChannel
{
    private readonly IEmailSender _sender;
    public EmailChannel(IEmailSender sender) => _sender = sender;

    public string Name => "email";

    public bool IsEnabled(Tenant tenant)
        => tenant.EmailNotificationsEnabled && !string.IsNullOrWhiteSpace(tenant.NotificationEmails);

    public async Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
    {
        var to = (tenant.NotificationEmails ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (to.Length == 0) return;

        var subject = $"[Valentino's] Nueva avería reportada: {n.AssetCodigo} ({n.Severidad})";
        var body =
            $"Se reportó una avería en el activo {n.AssetCodigo}.\n" +
            $"Severidad: {n.Severidad}\n" +
            $"Ubicación: {n.Ubicacion ?? "-"}\n" +
            $"Reportado por: {n.ReportadoPor ?? "-"}\n\n" +
            $"Descripción:\n{n.Descripcion}\n";

        await _sender.SendAsync(to, subject, body, ct);
    }
}
```

`src/Valentinos.Infrastructure/Notifications/DisabledChannels.cs`:

```csharp
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;

namespace Valentinos.Infrastructure.Notifications;

// Canales diseñados pero apagados por default. Encendibles desde la config del
// tenant en el futuro (Plan: activación de WhatsApp/SMS/Twilio). No-op por ahora.

public class InAppChannel : INotificationChannel
{
    public string Name => "inapp";
    public bool IsEnabled(Tenant tenant) => tenant.InAppNotificationsEnabled;
    public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class WhatsAppChannel : INotificationChannel
{
    public string Name => "whatsapp";
    public bool IsEnabled(Tenant tenant) => tenant.WhatsAppNotificationsEnabled;
    public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class SmsChannel : INotificationChannel
{
    public string Name => "sms";
    public bool IsEnabled(Tenant tenant) => tenant.SmsNotificationsEnabled;
    public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
        => Task.CompletedTask;
}
```

`src/Valentinos.Infrastructure/Notifications/NotificationService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Notifications;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly AppDbContext _db;

    public NotificationService(IEnumerable<INotificationChannel> channels, AppDbContext db)
    {
        _channels = channels;
        _db = db;
    }

    public async Task NotifyReportCreatedAsync(ReportCreatedNotification n, CancellationToken ct = default)
    {
        // Tenant no es ITenantOwned (sin query filter); se busca por Id directamente.
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == n.TenantId, ct);
        if (tenant is null) return;

        foreach (var channel in _channels)
        {
            if (!channel.IsEnabled(tenant)) continue;
            try
            {
                await channel.SendReportCreatedAsync(tenant, n, ct);
            }
            catch
            {
                // Best-effort: un canal que falla no debe romper el flujo ni los otros canales.
                // (En producción, loguear con el logger inyectado.)
            }
        }
    }
}
```

`src/Valentinos.Infrastructure/Notifications/SmtpEmailSender.cs`:

```csharp
using System.Net;
using System.Net.Mail;
using Valentinos.Application.Notifications;

namespace Valentinos.Infrastructure.Notifications;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    public SmtpEmailSender(SmtpOptions options) => _options = options;

    public async Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken ct = default)
    {
        // Apagado por default: sin SMTP configurado no se intenta enviar (no-op seguro).
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Host) || to.Count == 0)
            return;

        using var message = new MailMessage { From = new MailAddress(_options.From), Subject = subject, Body = body };
        foreach (var addr in to) message.To.Add(addr);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_options.User, _options.Password)
        };
        await client.SendMailAsync(message, ct);
    }
}
```

- [ ] **Step 6: Disparar la notificación desde ReportService**

En `src/Valentinos.Infrastructure/Reports/ReportService.cs`: agregar `using Valentinos.Application.Notifications;`, un campo `INotificationService _notifications`, incorporarlo al constructor, y tras `await _db.SaveChangesAsync(ct);` (antes del `return`):

```csharp
        try
        {
            await _notifications.NotifyReportCreatedAsync(new ReportCreatedNotification(
                report.TenantId, asset.Codigo, report.Severidad.ToString(),
                report.Descripcion, report.Ubicacion, report.ReportadoPor), ct);
        }
        catch
        {
            // La creación del reporte no debe fallar si la notificación falla.
        }
```

Constructor nuevo:

```csharp
    public ReportService(AppDbContext db, ITenantContext tenant, IFileStorage storage,
        INotificationService notifications)
    {
        _db = db;
        _tenant = tenant;
        _storage = storage;
        _notifications = notifications;
    }
```

> `report.TenantId` ya está estampado por `AppDbContext` tras `SaveChanges`.

- [ ] **Step 7: Actualizar el test de ReportService al nuevo constructor**

En `tests/Valentinos.Tests/Reports/ReportServiceTests.cs`, agregar un `INotificationService` no-op y pasarlo:

```csharp
    private sealed class NoopNotifications : Valentinos.Application.Notifications.INotificationService
    {
        public Task NotifyReportCreatedAsync(
            Valentinos.Application.Notifications.ReportCreatedNotification n, CancellationToken ct = default)
            => Task.CompletedTask;
    }
```

Y en `Build(...)`, cambiar la construcción:

```csharp
        var reports = new ReportService(db, ctx, fs, new NoopNotifications());
```

- [ ] **Step 8: Registrar notificaciones en DI**

En `src/Valentinos.Infrastructure/DependencyInjection.cs`, agregar `using Valentinos.Application.Notifications;`, `using Valentinos.Infrastructure.Notifications;`, y antes de `return services;`:

```csharp
        var smtpOptions = new SmtpOptions();
        configuration?.GetSection("Smtp").Bind(smtpOptions);
        services.AddSingleton(smtpOptions);
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<INotificationChannel, EmailChannel>();
        services.AddScoped<INotificationChannel, InAppChannel>();
        services.AddScoped<INotificationChannel, WhatsAppChannel>();
        services.AddScoped<INotificationChannel, SmsChannel>();
        services.AddScoped<INotificationService, NotificationService>();
```

- [ ] **Step 9: Generar la migración de config de notificaciones**

```bash
dotnet ef migrations add AddNotificationConfigToTenant \
  --project src/Valentinos.Infrastructure \
  --startup-project src/Valentinos.Api \
  --output-dir Persistence/Migrations
```

Verificar que agrega 5 columnas a `Tenants` (`EmailNotificationsEnabled` con default, `NotificationEmails` nullable, y los 3 flags bool), sin tocar otras tablas ni crear shadow FKs. No aplicar a la BD.

- [ ] **Step 10: Tests (pasan)**

Run: `dotnet test tests/Valentinos.Tests --filter "FullyQualifiedName~NotificationServiceTests|FullyQualifiedName~ReportServiceTests"`
Expected: PASS (NotificationServiceTests 3 + ReportServiceTests 3).

- [ ] **Step 11: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (verde). Luego:

```bash
git add -A
git commit -m "feat: notificaciones por canal (email activo, WhatsApp/SMS/InApp apagados) al crear reporte"
```

---

### Task 5: Endpoint público de reporte + rate limiting

**Files:**
- Create: `src/Valentinos.Api/Controllers/PublicReportsController.cs`
- Modify: `src/Valentinos.Api/Controllers/PublicTenantController.cs` (agregar GET de activo por código para la página pública)
- Modify: `src/Valentinos.Api/Program.cs` (rate limiter + mapear)
- Test: `tests/Valentinos.Tests/Api/PublicReportsEndpointTests.cs`

**Interfaces:**
- Consumes: `IReportService`, `AppDbContext`, `Severidad`.
- Produces:
  - `POST /api/public/{slug}/reports` (multipart/form-data): campos `descripcion`, `severidad` (`Leve|AMedias|NoFunciona`), `codigo` (código del activo), `ubicacion?`, `reportadoPor?`, `fotos?` (archivos). Devuelve `201` con `ReportDto`; `400` si faltan campos o hay demasiadas/grandes fotos; `404` si el activo no existe en el tenant.
  - `GET /api/public/{slug}/assets/{codigo}` → `200 { codigo, tipoNombre }` para que la página pública muestre qué activo se reporta; `404` si no existe.
  - Rate limiter fixed-window aplicado al endpoint de reporte.

**Nota:** el tenant lo resuelve el middleware del Plan 1 (ruta `/api/public/{slug}/...`), por lo que `ITenantContext` está seteado y el write-path fail-closed permite crear el `Report`. Límites: máx 5 fotos, máx 5 MB por foto (constantes en el controller).

- [ ] **Step 1: Escribir el test que falla**

`tests/Valentinos.Tests/Api/PublicReportsEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valentinos.Application.Abstractions;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Api;

public class PublicReportsEndpointTests : IClassFixture<PublicReportsEndpointTests.Factory>
{
    private readonly Factory _factory;
    public PublicReportsEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task PostReport_ActivoExistente_Crea201()
    {
        var client = _factory.CreateClient();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("No aspira"), "descripcion" },
            { new StringContent("NoFunciona"), "severidad" },
            { new StringContent(Factory.SeedCodigo), "codigo" },
            { new StringContent("Piso 2"), "ubicacion" },
            { new StringContent("Ana"), "reportadoPor" },
        };
        var resp = await client.PostAsync("/api/public/mastercorp/reports", form);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task PostReport_ActivoInexistente_Devuelve404()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("x"), "descripcion" },
            { new StringContent("Leve"), "severidad" },
            { new StringContent("VAC-999"), "codigo" },
        };
        var resp = await client.PostAsync("/api/public/mastercorp/reports", form);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PostReport_SinDescripcion_Devuelve400()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new StringContent(""), "descripcion" },
            { new StringContent("Leve"), "severidad" },
            { new StringContent(Factory.SeedCodigo), "codigo" },
        };
        var resp = await client.PostAsync("/api/public/mastercorp/reports", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAsset_Existente_Devuelve200()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/public/mastercorp/assets/{Factory.SeedCodigo}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    public class Factory : WebApplicationFactory<Program>
    {
        public const string SeedCodigo = "VAC-001";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var toRemove = services.Where(d =>
                    d.ServiceType.FullName != null &&
                    d.ServiceType.FullName.Contains("AppDbContext")).ToList();
                foreach (var d in toRemove) services.Remove(d);

                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("PublicReportsTests"));

                using var scope = services.BuildServiceProvider().CreateScope();
                var sp = scope.ServiceProvider;
                var tenantCtx = sp.GetRequiredService<ITenantContext>();

                // Sembrar tenant + tipo + activo. El write-path exige tenant en contexto
                // para entidades ITenantOwned (AssetType/Asset), así que se setea primero.
                var db = sp.GetRequiredService<AppDbContext>();
                var tenant = Tenant.Create("mastercorp", "MasterCorp");
                db.Tenants.Add(tenant);
                db.SaveChanges();

                tenantCtx.Set(tenant.Id);
                var tipo = AssetType.Create("Aspiradora", "VAC");
                db.AssetTypes.Add(tipo);
                db.SaveChanges();
                tipo.CorrelativoActual = 1;
                db.Assets.Add(new Asset { AssetTypeId = tipo.Id, Codigo = SeedCodigo });
                db.SaveChanges();
            });
        }
    }
}
```

> Nota: el `ITenantContext` resuelto en el scope del seeding es el mismo tipo scoped que usa el request; se setea manualmente solo para la siembra. Durante los requests reales, el middleware lo setea por slug.

- [ ] **Step 2: Verificar que falla**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~PublicReportsEndpointTests`
Expected: FAIL — endpoints no existen.

- [ ] **Step 3: Implementar el controller de reportes**

`src/Valentinos.Api/Controllers/PublicReportsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Valentinos.Application.Reports;
using Valentinos.Domain.Enums;

namespace Valentinos.Api.Controllers;

[ApiController]
[Route("api/public/{slug}/reports")]
public class PublicReportsController : ControllerBase
{
    private const int MaxPhotos = 5;
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    private readonly IReportService _reports;
    public PublicReportsController(IReportService reports) => _reports = reports;

    [HttpPost]
    [EnableRateLimiting("public-reports")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Create(
        string slug,
        [FromForm] string codigo,
        [FromForm] string descripcion,
        [FromForm] string severidad,
        [FromForm] string? ubicacion,
        [FromForm] string? reportadoPor,
        [FromForm] IFormFileCollection? fotos,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
            return BadRequest("La descripción es obligatoria.");
        if (!Enum.TryParse<Severidad>(severidad, ignoreCase: true, out var sev))
            return BadRequest("Severidad inválida.");
        if (string.IsNullOrWhiteSpace(codigo))
            return BadRequest("El código del activo es obligatorio.");

        var files = fotos ?? (IFormFileCollection)new FormFileCollection();
        if (files.Count > MaxPhotos)
            return BadRequest($"Máximo {MaxPhotos} fotos por reporte.");

        var photos = new List<ReportPhotoInput>();
        foreach (var f in files)
        {
            if (f.Length <= 0) continue;
            if (f.Length > MaxPhotoBytes)
                return BadRequest($"Cada foto no puede superar {MaxPhotoBytes / (1024 * 1024)} MB.");
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            photos.Add(new ReportPhotoInput(ms.ToArray(), f.ContentType));
        }

        try
        {
            var dto = await _reports.CreateReportAsync(new CreateReportRequest(
                codigo, descripcion, sev, ubicacion, reportadoPor, photos), ct);
            return StatusCode(StatusCodes.Status201Created, dto);
        }
        catch (InvalidOperationException)
        {
            return NotFound("El activo no existe para este tenant.");
        }
    }
}
```

- [ ] **Step 4: Agregar el GET de activo por código (para la página pública)**

En `src/Valentinos.Api/Controllers/PublicTenantController.cs`, agregar (dentro de la clase). Requiere `using Valentinos.Domain.Entities;` si no está:

```csharp
    [HttpGet("assets/{codigo}")]
    public async Task<IActionResult> GetAsset(string slug, string codigo)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();

        var asset = await _db.Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.Codigo == codigo);
        if (asset is null) return NotFound();

        var tipo = await _db.AssetTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == asset.AssetTypeId);

        return Ok(new { codigo = asset.Codigo, tipoNombre = tipo?.Nombre });
    }
```

> Se usa `IgnoreQueryFilters` + filtro explícito por `tenant.Id` porque este GET puede ejecutarse antes de que el middleware resuelva el tenant (mismo patrón que `whoami`). Es de solo lectura y acotado al tenant del slug.

- [ ] **Step 5: Configurar el rate limiter y verificar el mapeo en Program.cs**

En `src/Valentinos.Api/Program.cs`, agregar `using System.Threading.RateLimiting;`. Registrar el limitador (antes de `var app = builder.Build();`):

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("public-reports", o =>
    {
        o.PermitLimit = 20;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
});
```

Y activar el middleware (después de `app.UseMiddleware<TenantResolutionMiddleware>();` y antes de `app.MapControllers();`):

```csharp
app.UseRateLimiter();
```

- [ ] **Step 6: Tests del endpoint (pasan)**

Run: `dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~PublicReportsEndpointTests`
Expected: PASS (4 tests: 201, 404, 400, GET 200).

- [ ] **Step 7: Suite completa + commit**

Run: `dotnet test Valentinos.sln` (verde). Luego:

```bash
git add -A
git commit -m "feat: endpoint público de reporte (multipart) con rate limiting y GET de activo"
```

---

## Self-Review

**Spec coverage (Plan 3 scope):**
- Reporte con descripción, severidad, piso, fotos → entidades (Task 1) + servicio (Task 3) + endpoint (Task 5) ✓
- Fotos vía almacenamiento abstraído (`IFileStorage`, disco local) → Task 2 ✓
- Alerta por email al crear reporte + abstracción de canales (WhatsApp/SMS/In-App apagados) → Task 4 ✓
- Endpoint público anónimo (el que abre el QR) con rate limiting → Task 5 ✓
- Aislamiento por tenant → heredado (entidades `ITenantOwned`, resolución por slug); el servicio resuelve el activo dentro del tenant.
- **Fuera de alcance (planes siguientes):** ciclo de vida del reporte (Nuevo→EnReparación→Resuelto) y KPIs → Plan 4; **reportes automáticos** diario/semanal/mensual → Plan 5; servir/descargar las fotos y el frontend → Planes 4/6. Activación real de WhatsApp/SMS/Twilio → futuro.

**Placeholder scan:** sin TBD/TODO; código completo en cada paso.

**Type consistency:** `IReportService.CreateReportAsync(CreateReportRequest)` con `ReportPhotoInput(Content, ContentType)` usado igual en servicio y endpoint. `ReportService` gana `INotificationService` en Task 4 y el test de Task 3 se actualiza en la misma Task 4 (Step 7) para no dejar el build roto entre tasks. `INotificationChannel`/`ReportCreatedNotification` consistentes entre canales, servicio y disparo. `AddInfrastructure` no cambia de firma (ya acepta `IConfiguration?`).

**Notas de entorno/seguridad:** el endpoint público es anónimo por diseño (lo abre el QR) pero con rate limiting y límites de tamaño/cantidad de fotos; no expone datos de otros tenants. El envío SMTP está apagado por default (no-op sin configuración), coherente con "email activo pero sin credenciales aún". Las migraciones no se aplican a la BD.
