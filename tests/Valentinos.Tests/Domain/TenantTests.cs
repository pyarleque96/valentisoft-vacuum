using Valentinos.Domain.Entities;
using Xunit;

namespace Valentinos.Tests.Domain;

public class TenantTests
{
    [Fact]
    public void Create_NormalizaSlugAMinusculasSinEspacios()
    {
        var tenant = Tenant.Create("  Master Corp ", "MasterCorp");

        Assert.Equal("master-corp", tenant.Slug);
        Assert.Equal("MasterCorp", tenant.Nombre);
        Assert.NotEqual(Guid.Empty, tenant.Id);
    }

    [Fact]
    public void Create_SlugVacio_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => Tenant.Create("   ", "MasterCorp"));
    }
}
