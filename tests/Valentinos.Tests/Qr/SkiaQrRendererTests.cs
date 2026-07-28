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
