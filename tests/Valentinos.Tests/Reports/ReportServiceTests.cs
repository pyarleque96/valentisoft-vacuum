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

    private sealed class NoopNotifications : Valentinos.Application.Notifications.INotificationService
    {
        public Task NotifyReportCreatedAsync(
            Valentinos.Application.Notifications.ReportCreatedNotification n, CancellationToken ct = default)
            => Task.CompletedTask;
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
        var reports = new ReportService(db, ctx, fs, new NoopNotifications());
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
