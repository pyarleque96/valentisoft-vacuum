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

        // Semilla realista: cada tenant escribe sus propias filas bajo su contexto.
        var ctxA = new FakeTenantContext();
        ctxA.Set(tenantA);
        using (var db = new TestDbContext(options, ctxA))
        {
            db.Widgets.Add(new Widget { Nombre = "A" });
            db.SaveChanges();
        }

        var ctxB = new FakeTenantContext();
        ctxB.Set(tenantB);
        using (var db = new TestDbContext(options, ctxB))
        {
            db.Widgets.Add(new Widget { Nombre = "B" });
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
    public void SaveChanges_SinTenantEnContexto_LanzaExcepcion()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var ctx = new FakeTenantContext(); // sin tenant
        using var db = new TestDbContext(options, ctx);
        db.Widgets.Add(new Widget { Nombre = "SinContexto" });

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void SaveChanges_TenantExplicitoAjeno_LanzaExcepcion()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var tenantActual = Guid.NewGuid();
        var otroTenant = Guid.NewGuid();
        var ctx = new FakeTenantContext();
        ctx.Set(tenantActual);

        using var db = new TestDbContext(options, ctx);
        db.Widgets.Add(new Widget { TenantId = otroTenant, Nombre = "Ajeno" });

        Assert.Throws<UnauthorizedAccessException>(() => db.SaveChanges());
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
