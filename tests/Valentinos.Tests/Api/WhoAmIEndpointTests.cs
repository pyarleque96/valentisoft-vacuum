using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Api;

public class WhoAmIEndpointTests : IClassFixture<WhoAmIEndpointTests.Factory>
{
    private readonly Factory _factory;
    public WhoAmIEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task WhoAmI_TenantExistente_Devuelve200ConDatos()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/public/mastercorp/whoami");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WhoAmIResponse>();
        Assert.Equal("mastercorp", body!.Slug);
        Assert.Equal("MasterCorp", body.Nombre);
    }

    [Fact]
    public async Task WhoAmI_TenantInexistente_Devuelve404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/public/noexiste/whoami");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    public record WhoAmIResponse(Guid TenantId, string Slug, string Nombre);

    // Factory que reemplaza SQL Server por InMemory y siembra MasterCorp
    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                // Quita todos los descriptores relacionados con el proveedor SqlServer
                // registrado por AddInfrastructure (no basta con remover solo
                // DbContextOptions<AppDbContext>: EF registra también
                // IDbContextOptionsConfiguration<AppDbContext> y otros servicios internos
                // ligados al proveedor; si quedan, InMemory y SqlServer chocan al construir
                // el ServiceProvider interno de EF).
                var descriptores = services.Where(d =>
                    d.ServiceType.FullName is not null &&
                    d.ServiceType.FullName.Contains(nameof(AppDbContext))).ToList();
                foreach (var d in descriptores) services.Remove(d);

                services.AddDbContext<AppDbContext>(o =>
                    o.UseInMemoryDatabase("WhoAmITests"));
            });
        }

        // La siembra se hace después de construir el host (no dentro de
        // ConfigureServices, donde llamar a BuildServiceProvider() crea un
        // ServiceProvider descartable ajeno al del host y dispara el analizador
        // ASP0000).
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);

            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            DbSeeder.SeedAsync(db).GetAwaiter().GetResult();

            return host;
        }
    }
}
