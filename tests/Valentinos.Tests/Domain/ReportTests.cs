using Valentinos.Domain.Entities;
using Valentinos.Domain.Enums;
using Xunit;

namespace Valentinos.Tests.Domain;

public class ReportTests
{
    [Fact]
    public void Create_ReporteNuevo_TieneEstadoNuevoYCampos()
    {
        var assetId = Guid.NewGuid();
        var r = Report.Create(assetId, "No enciende", Severidad.NoFunciona, "Piso 3", "Ana");

        Assert.Equal(assetId, r.AssetId);
        Assert.Equal("No enciende", r.Descripcion);
        Assert.Equal(Severidad.NoFunciona, r.Severidad);
        Assert.Equal("Piso 3", r.Ubicacion);
        Assert.Equal("Ana", r.ReportadoPor);
        Assert.Equal(ReportStatus.Nuevo, r.Estado);
        Assert.Null(r.ResolvedAt);
    }

    [Fact]
    public void Create_DescripcionVacia_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(
            () => Report.Create(Guid.NewGuid(), "   ", Severidad.Leve, null, null));
    }
}
