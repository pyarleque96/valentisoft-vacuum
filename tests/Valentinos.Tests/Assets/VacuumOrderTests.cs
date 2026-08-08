using Valentinos.Api.Assets;
using Xunit;

namespace Valentinos.Tests.Assets;

// Orden de presentación de los vacuums: los de nombre propio (janitorial) primero,
// después los numerados. El orden alfabético crudo los ponía al final porque los
// dígitos ordenan antes que las letras.
public class VacuumOrderTests
{
    [Fact]
    public void Sort_LosDeNombrePropioVanPrimero_LuegoLosNumerados()
    {
        var codigos = new[] { "VAC-002", "VAC-TIMESQUARE", "VAC-001", "VAC-FRONTDESK", "VAC-011" };

        var orden = VacuumOrder.Sort(codigos);

        Assert.Equal(
            new[] { "VAC-FRONTDESK", "VAC-TIMESQUARE", "VAC-001", "VAC-002", "VAC-011" },
            orden);
    }

    [Fact]
    public void Sort_LosNumeradosVanEnOrdenNumerico_NoAlfabetico()
    {
        // Alfabéticamente "VAC-10" iría antes que "VAC-9"; numéricamente no.
        var codigos = new[] { "VAC-10", "VAC-9", "VAC-1" };

        Assert.Equal(new[] { "VAC-1", "VAC-9", "VAC-10" }, VacuumOrder.Sort(codigos));
    }

    [Fact]
    public void Sort_VariosDeNombrePropio_QuedanAlfabeticosEntreSi()
    {
        var codigos = new[] { "VAC-TIMESQUARE", "VAC-LOBBY", "VAC-FRONTDESK" };

        Assert.Equal(
            new[] { "VAC-FRONTDESK", "VAC-LOBBY", "VAC-TIMESQUARE" },
            VacuumOrder.Sort(codigos));
    }

    [Fact]
    public void Sort_ListaVacia_DevuelveListaVacia()
    {
        Assert.Empty(VacuumOrder.Sort(System.Array.Empty<string>()));
    }

    [Fact]
    public void Numero_DevuelveElCorrelativo_YNullSiNoEsNumerado()
    {
        Assert.Equal(7, VacuumOrder.Numero("VAC-007"));
        Assert.Equal(11, VacuumOrder.Numero("VAC-011"));
        Assert.Null(VacuumOrder.Numero("VAC-FRONTDESK"));
        Assert.Null(VacuumOrder.Numero("SinGuion"));
    }
}
