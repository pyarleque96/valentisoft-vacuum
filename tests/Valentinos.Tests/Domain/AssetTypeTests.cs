using Valentinos.Domain.Entities;
using Xunit;

namespace Valentinos.Tests.Domain;

public class AssetTypeTests
{
    [Fact]
    public void Create_NormalizaPrefijoAMayusculasSinEspacios()
    {
        var t = AssetType.Create("Aspiradora", "  vac ");

        Assert.Equal("Aspiradora", t.Nombre);
        Assert.Equal("VAC", t.Prefijo);
        Assert.Equal(0, t.CorrelativoActual);
        Assert.NotEqual(Guid.Empty, t.Id);
    }

    [Fact]
    public void Create_PrefijoVacio_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => AssetType.Create("Aspiradora", "   "));
    }
}
