using System.Text;
using Valentinos.Application.Qr;
using Valentinos.Infrastructure.Qr;
using Xunit;

namespace Valentinos.Tests.Qr;

public class SkiaQrSheetRendererTests
{
    private static SkiaQrSheetRenderer Build()
        => new(new SkiaQrRenderer(new QrOptions { BaseUrl = "https://app.valentinos.com" }));

    [Fact]
    public void RenderPdf_VariosItems_ProduceUnPdfValido()
    {
        var sheet = Build();
        var items = new List<QrSheetItem>
        {
            new("mastercorp", "VAC-001", null),
            new("mastercorp", "VAC-002", null),
            new("mastercorp", "VAC-003", null),
            new("mastercorp", "VAC-004", null),
        };

        var pdf = sheet.RenderPdf(items);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000, "El PDF debería tener contenido no trivial.");
        var header = Encoding.ASCII.GetString(pdf, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void RenderPdf_ListaVacia_ProducePdfValido()
    {
        var sheet = Build();
        var pdf = sheet.RenderPdf(new List<QrSheetItem>());
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }
}
