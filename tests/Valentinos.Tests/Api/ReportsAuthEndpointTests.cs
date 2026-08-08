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
