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

// El generador de QR es una vista pública por site: vive en /{siteSlug}/qr, no bajo
// /admin/. La URL vieja sigue redirigiendo para no romper links ya compartidos.
public class SiteQrEndpointTests : IClassFixture<SiteQrEndpointTests.Factory>
{
    private readonly Factory _factory;
    public SiteQrEndpointTests(Factory factory) => _factory = factory;

    private System.Net.Http.HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new System.Uri("http://mastercorp.valentisoft.com")
        });

    [Fact]
    public async Task GetSiteQr_SinLogin_Devuelve200()
    {
        var resp = await NoRedirectClient().GetAsync("/SiteUno/qr");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetSiteQr_SlugInexistente_Devuelve404()
    {
        var resp = await NoRedirectClient().GetAsync("/NoExiste/qr");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetSiteQr_MuestraLosVacuumsDeSuSiteYNoLosDeOtro()
    {
        var html = await _factory
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new System.Uri("http://mastercorp.valentisoft.com")
            })
            .GetStringAsync("/SiteUno/qr");

        Assert.Contains("VAC-SOLOUNO", html);
        Assert.DoesNotContain("VAC-SOLODOS", html);
    }

    [Fact]
    public async Task UrlVieja_DeAdmin_RedirigeALaNueva()
    {
        var resp = await NoRedirectClient().GetAsync("/admin/sites/SiteUno/qr");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/SiteUno/qr", resp.Headers.Location?.OriginalString);
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
                    o.UseInMemoryDatabase("SiteQrEndpointTests"));
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
            var uno = new Site { Code = "001", Slug = "SiteUno" };
            var dos = new Site { Code = "002", Slug = "SiteDos" };
            db.Sites.AddRange(uno, dos);
            db.SaveChanges();

            var tipo = AssetType.Create("Vacuum", "VAC");
            db.AssetTypes.Add(tipo);
            db.SaveChanges();

            db.Assets.AddRange(
                new Asset { AssetTypeId = tipo.Id, SiteId = uno.Id, Codigo = "VAC-SOLOUNO" },
                new Asset { AssetTypeId = tipo.Id, SiteId = dos.Id, Codigo = "VAC-SOLODOS" });
            db.SaveChanges();

            return host;
        }
    }
}
