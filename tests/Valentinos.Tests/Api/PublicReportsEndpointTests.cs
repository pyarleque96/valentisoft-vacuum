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
                    o.UseInMemoryDatabase("PublicReportsTests"));
            });
        }

        // La siembra se hace después de construir el host (no dentro de
        // ConfigureServices, donde llamar a BuildServiceProvider() crea un
        // ServiceProvider descartable ajeno al del host y dispara el analizador
        // ASP0000). El write-path exige tenant en contexto para entidades
        // ITenantOwned (AssetType/Asset), así que se setea manualmente en este
        // scope de siembra antes de guardarlas.
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
            var tipo = AssetType.Create("Aspiradora", "VAC");
            db.AssetTypes.Add(tipo);
            db.SaveChanges();
            tipo.CorrelativoActual = 1;
            db.Assets.Add(new Asset { AssetTypeId = tipo.Id, Codigo = SeedCodigo });
            db.SaveChanges();

            return host;
        }
    }
}
