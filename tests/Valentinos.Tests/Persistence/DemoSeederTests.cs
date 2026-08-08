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
    public async Task Seed_CreaElSite200BOYConSus28VacuumsYSus14Empleados()
    {
        var (db, ctx) = NewDbConTenant();

        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        var site = await db.Sites.IgnoreQueryFilters().FirstAsync(s => s.Slug == "Vn5Tq");
        Assert.Equal("200BOY", site.Code);
        Assert.Equal(
            "sherri.clapper@mastercorp.com, jodi.hazel@mastercorp.com, "
            + "theresa.schneider@mastercorp.com, marianne.watkins@mastercorp.com",
            site.Emails);

        var vacs = await db.Assets.IgnoreQueryFilters()
            .Where(a => a.SiteId == site.Id).Select(a => a.Codigo).ToListAsync();
        Assert.Equal(28, vacs.Count);
        Assert.Contains("VAC-001", vacs);
        Assert.Contains("VAC-028", vacs);
        Assert.DoesNotContain("VAC-029", vacs);

        var emps = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.SiteId == site.Id).Select(e => e.Nombre).ToListAsync();
        Assert.Equal(14, emps.Count);
        Assert.Contains("Alfaro Flores, Kevin E", emps);
        Assert.Contains("Marroquin Marroquin, Sirli Y", emps);
        Assert.Contains("Strickler, Dyllan", emps);
    }

    [Fact]
    public async Task Seed_EsIdempotente_NoDuplicaNada()
    {
        var (db, ctx) = NewDbConTenant();

        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");
        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        // Solo 069 y 127 HCC: los placeholders 003 y 004 se eliminaron del seeder.
        Assert.Equal(3, await db.Sites.IgnoreQueryFilters().CountAsync());      // 069, 127 HCC y 200BOY
        Assert.Equal(57, await db.Assets.IgnoreQueryFilters().CountAsync());    // 16 + 13 + 28
        Assert.Equal(35, await db.Employees.IgnoreQueryFilters().CountAsync()); // 16 + 5 + 14
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

    [Fact]
    public async Task Seed_SiElAdminBorraLosEmailsDespuesDeLaMigracion_UnReinicioNoLosResucita()
    {
        var (db, ctx) = NewDbConTenant();

        // Primer arranque: la migración "002" -> "127 HCC" siembra los emails.
        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");
        var site = await db.Sites.IgnoreQueryFilters().FirstAsync(s => s.Slug == "Kp7Qm");
        Assert.Equal(Emails127, site.Emails);

        // El admin borra los destinatarios desde el panel (AdminController.SaveSite guarda null).
        site.Emails = null;
        await db.SaveChangesAsync();

        // Segundo arranque (reinicio de la app): el seeder no debe volver a sembrarlos,
        // porque la transición "002" -> "127 HCC" ya ocurrió.
        await DemoSeeder.SeedAsync(db, ctx, "christopher.davey@mastercorp.com");

        site = await db.Sites.IgnoreQueryFilters().FirstAsync(s => s.Slug == "Kp7Qm");
        Assert.Null(site.Emails);
    }
}
