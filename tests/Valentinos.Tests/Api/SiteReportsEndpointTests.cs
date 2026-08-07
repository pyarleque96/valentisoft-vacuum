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
        public Factory()
        {
            // Host de un subdominio real de tenant: sin esto, SlugResolver.FromHost
            // devuelve null para "localhost" y el middleware nunca fija el tenant en
            // contexto (Tid queda en Guid.Empty). Con Tid vacío, una query filtrada
            // por TenantId (p. ej. la del dropdown en /reports) devolvería una lista
            // vacía TAMBIÉN en el test, y el test de aislamiento pasaría sin haber
            // probado nada.
            ClientOptions.BaseAddress = new Uri("http://mastercorp.valentisoft.com");
        }

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
