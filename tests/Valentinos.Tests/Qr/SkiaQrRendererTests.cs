using SkiaSharp;
using Valentinos.Application.Qr;
using Valentinos.Infrastructure.Qr;
using Xunit;
using ZXing.SkiaSharp;

namespace Valentinos.Tests.Qr;

public class SkiaQrRendererTests
{
    private static SkiaQrRenderer Build(string baseUrl = "https://app.valentinos.com")
        => new(new QrOptions { BaseUrl = baseUrl });

    [Fact]
    public void BuildUrl_ComponeLaUrlPublicaDeReporte()
    {
        var r = Build("https://app.valentinos.com");
        Assert.Equal("https://app.valentinos.com/r/mastercorp/VAC-001",
            r.BuildUrl("mastercorp", "VAC-001"));
    }

    // La etiqueta se dibuja centrada bajo el QR, y el ancho de la imagen depende de
    // cuántos módulos necesite la URL: a tamaño fijo, una etiqueta larga se sale.
    // Si desbordara pintaría los extremos de la franja inferior; que sigan blancos es
    // la prueba de que el ajuste de tamaño hizo su trabajo.
    //
    // El primer caso es deliberadamente más largo que cualquier etiqueta real: a 34px
    // mide más que la imagen, así que ES el caso que falla si se quita el ajuste. Las
    // etiquetas reales que le siguen caben holgadas hoy (HOUSEKEEPING (NO VACUUM) mide
    // 512px en una imagen de 588) y están aquí para que se note si alguna deja de caber.
    [Theory]
    [InlineData("HOUSEKEEPING (NO VACUUM) MUY MUY LARGO PARA PROBAR")]
    [InlineData("HOUSEKEEPING (NO VACUUM)")]
    [InlineData("VAC-TIMESQUARE")]
    [InlineData("VAC-001")]
    public void RenderPngForUrl_LaEtiquetaNoDesbordaLaImagen(string etiqueta)
    {
        var png = Build().RenderPngForUrl("https://mastercorp.valentisoft.com/Kp7Qm/f/hk",
                                          etiqueta, logoPng: null);

        using var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);

        const int labelHeight = 64;          // franja inferior reservada al texto
        var stripTop = bmp.Height - labelHeight;
        for (var y = stripTop; y < bmp.Height; y++)
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(SKColors.White, bmp.GetPixel(x, y));
                Assert.Equal(SKColors.White, bmp.GetPixel(bmp.Width - 1 - x, y));
            }
    }

    [Fact]
    public void RenderPng_ProduceQrDecodificableConLaUrl()
    {
        var r = Build();
        var png = r.RenderPng(new QrRenderRequest("mastercorp", "VAC-001", LogoPng: null));

        Assert.NotNull(png);
        Assert.True(png.Length > 0);

        using var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);

        var reader = new BarcodeReader();
        var result = reader.Decode(bmp);
        Assert.NotNull(result);
        Assert.Equal("https://app.valentinos.com/r/mastercorp/VAC-001", result.Text);
    }

    [Fact]
    public void RenderPng_ConLogo_SigueSiendoDecodificable()
    {
        var r = Build();
        var logo = MakeSolidPng(80, 80, SKColors.RoyalBlue);
        var png = r.RenderPng(new QrRenderRequest("mastercorp", "VAC-007", logo));

        using var bmp = SKBitmap.Decode(png);
        var reader = new BarcodeReader();
        var result = reader.Decode(bmp);
        Assert.NotNull(result);
        Assert.Equal("https://app.valentinos.com/r/mastercorp/VAC-007", result.Text);
    }

    private static byte[] MakeSolidPng(int w, int h, SKColor color)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        surface.Canvas.Clear(color);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
