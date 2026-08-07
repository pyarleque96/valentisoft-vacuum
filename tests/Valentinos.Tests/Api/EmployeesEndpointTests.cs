using System.Net;
using System.Net.Http.Json;
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

// El autocompletar del formulario público debe sugerir SOLO a los empleados del
// site cuyo QR se escaneó: el site 127 no puede ver los nombres del 069.
public class EmployeesEndpointTests : IClassFixture<EmployeesEndpointTests.Factory>
{
    private readonly Factory _factory;
    public EmployeesEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Employees_DevuelveSoloLosDelSiteConsultado()
    {
        var client = _factory.CreateClient();

        var uno = await client.GetFromJsonAsync<List<string>>("/api/public/SiteUno/employees");
        var dos = await client.GetFromJsonAsync<List<string>>("/api/public/SiteDos/employees");

        Assert.Equal(new[] { "Alfa, Ana" }, uno);
        Assert.Equal(new[] { "Beta, Bruno" }, dos);
    }

    [Fact]
    public async Task Employees_SiteInexistente_Devuelve404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/public/NoExiste/employees");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
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
                    o.UseInMemoryDatabase("EmployeesEndpointTests"));
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

            // Site y Employee son ITenantOwned: el write-path exige tenant en contexto.
            tenantCtx.Set(tenant.Id);
            var uno = new Site { Code = "001", Slug = "SiteUno" };
            var dos = new Site { Code = "002", Slug = "SiteDos" };
            db.Sites.AddRange(uno, dos);
            db.SaveChanges();

            db.Employees.AddRange(
                new Employee { SiteId = uno.Id, Nombre = "Alfa, Ana" },
                new Employee { SiteId = dos.Id, Nombre = "Beta, Bruno" });
            db.SaveChanges();

            return host;
        }
    }
}
