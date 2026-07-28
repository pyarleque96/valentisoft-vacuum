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

    [Fact]
    public async Task ListAssetsAsync_OrdenaPorCreacionNoLexicalmente_MasAllaDe999()
    {
        var (db, svc) = Build(Guid.NewGuid());
        var tipo = await svc.CreateAssetTypeAsync(new CreateAssetTypeRequest("Aspiradora", "VAC"));

        // Forzamos el correlativo a 998 para que los siguientes activos crucen la
        // frontera de 3 a 4 dígitos (VAC-999 -> VAC-1000 -> VAC-1001).
        var tipoEntity = await db.AssetTypes.SingleAsync(t => t.Id == tipo.Id);
        tipoEntity.CorrelativoActual = 998;
        await db.SaveChangesAsync();

        var a999 = await svc.CreateAssetAsync(new CreateAssetRequest(tipo.Id, null));
        var a1000 = await svc.CreateAssetAsync(new CreateAssetRequest(tipo.Id, null));
        var a1001 = await svc.CreateAssetAsync(new CreateAssetRequest(tipo.Id, null));

        Assert.Equal("VAC-999", a999.Codigo);
        Assert.Equal("VAC-1000", a1000.Codigo);
        Assert.Equal("VAC-1001", a1001.Codigo);

        var lista = await svc.ListAssetsAsync();

        // Orden de creación esperado. Bajo el orden lexical anterior (OrderBy(Codigo))
        // el resultado habría sido VAC-1000, VAC-1001, VAC-999, porque "1" < "9"
        // en comparación de cadenas.
        Assert.Equal(new[] { "VAC-999", "VAC-1000", "VAC-1001" }, lista.Select(a => a.Codigo));
    }
}
