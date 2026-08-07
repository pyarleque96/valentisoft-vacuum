using Valentinos.Api.Multitenancy;
using Xunit;

namespace Valentinos.Tests.Multitenancy;

// Cobertura de SlugResolver.FromHost: es el punto de entrada de la resolución de
// tenant por subdominio (TenantResolutionMiddleware). Un null equivocado ahí
// deja de scopear el tenant en silencio.
public class SlugExtractionTests
{
    private const string BaseDomain = "valentisoft.com";

    [Fact]
    public void FromHost_SubdominioDeTenant_DevuelveElSlug()
    {
        Assert.Equal("mastercorp", SlugResolver.FromHost("mastercorp.valentisoft.com", BaseDomain));
    }

    [Fact]
    public void FromHost_ApexDomain_DevuelveNull()
    {
        Assert.Null(SlugResolver.FromHost("valentisoft.com", BaseDomain));
    }

    [Theory]
    [InlineData("www")]
    [InlineData("app")]
    [InlineData("api")]
    [InlineData("admin")]
    public void FromHost_SubdominiosReservados_DevuelveNull(string reserved)
    {
        Assert.Null(SlugResolver.FromHost($"{reserved}.valentisoft.com", BaseDomain));
    }

    [Fact]
    public void FromHost_HostQueNoCuelgaDeBaseDomain_DevuelveNull()
    {
        // p. ej. un túnel de desarrollo (*.trycloudflare.com).
        Assert.Null(SlugResolver.FromHost("random-tunnel.trycloudflare.com", BaseDomain));
    }

    [Fact]
    public void FromHost_HostConPuerto_IgnoraElPuertoYExtraeElSlug()
    {
        Assert.Equal("mastercorp", SlugResolver.FromHost("mastercorp.valentisoft.com:5001", BaseDomain));
    }

    [Fact]
    public void FromHost_HostNulo_DevuelveNull()
    {
        Assert.Null(SlugResolver.FromHost(null, BaseDomain));
    }

    [Fact]
    public void FromHost_HostVacio_DevuelveNull()
    {
        Assert.Null(SlugResolver.FromHost("", BaseDomain));
    }

    [Fact]
    public void FromHost_BaseDomainNulo_DevuelveNull()
    {
        Assert.Null(SlugResolver.FromHost("mastercorp.valentisoft.com", null));
    }

    [Fact]
    public void FromHost_Localhost_DevuelveNull()
    {
        // localhost no cuelga de baseDomain -> null (comportamiento documentado).
        Assert.Null(SlugResolver.FromHost("localhost", BaseDomain));
    }

    [Fact]
    public void FromHost_SubdominioAnidado_TomaLaEtiquetaMasALaIzquierda()
    {
        Assert.Equal("foo", SlugResolver.FromHost("foo.bar.valentisoft.com", BaseDomain));
    }

    [Fact]
    public void FromHost_EsCaseInsensitive()
    {
        Assert.Equal("mastercorp", SlugResolver.FromHost("MasterCorp.ValentiSoft.COM", BaseDomain));
    }
}
