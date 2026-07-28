using QRCoder;
using SkiaSharp;
using Valentinos.Application.Qr;

namespace Valentinos.Infrastructure.Qr;

public class SkiaQrRenderer : IQrRenderer
{
    private const int ModulePixels = 12;   // tamaño de cada módulo del QR en px
    private const int LabelHeight = 64;     // franja inferior para el código impreso

    private readonly QrOptions _options;

    public SkiaQrRenderer(QrOptions options) => _options = options;

    public string BuildUrl(string slug, string codigo)
        => $"{_options.BaseUrl.TrimEnd('/')}/r/{slug}/{codigo}";

    public byte[] RenderPng(QrRenderRequest req)
        => RenderPngForUrl(BuildUrl(req.Slug, req.Codigo), req.Codigo, req.LogoPng);

    public byte[] RenderPngForUrl(string url, string codeLabel, byte[]? logoPng)
    {
        // 1) Matriz del QR con máxima corrección de errores (tolera el logo central).
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        var matrix = data.ModuleMatrix;              // List<BitArray>, incluye quiet zone
        var modules = matrix.Count;
        var side = modules * ModulePixels;

        var imageInfo = new SKImageInfo(side, side + LabelHeight);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // 2) Dibujar módulos negros.
        using (var black = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill })
        {
            for (var y = 0; y < modules; y++)
            {
                var row = matrix[y];
                for (var x = 0; x < modules; x++)
                {
                    if (row[x])
                        canvas.DrawRect(x * ModulePixels, y * ModulePixels, ModulePixels, ModulePixels, black);
                }
            }
        }

        // 3) Logo central opcional con recuadro blanco de respaldo (~22% del lado).
        if (logoPng is { Length: > 0 })
        {
            using var logo = SKBitmap.Decode(logoPng);
            if (logo is not null)
            {
                var logoSide = (int)(side * 0.22);
                var pad = logoSide / 6;
                var cx = side / 2f;
                var cy = side / 2f;
                var boxRect = new SKRect(cx - logoSide / 2f - pad, cy - logoSide / 2f - pad,
                                         cx + logoSide / 2f + pad, cy + logoSide / 2f + pad);
                using (var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
                    canvas.DrawRect(boxRect, white);

                var logoRect = new SKRect(cx - logoSide / 2f, cy - logoSide / 2f,
                                          cx + logoSide / 2f, cy + logoSide / 2f);
                canvas.DrawBitmap(logo, logoRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }
        }

        // 4) Código impreso centrado en la franja inferior.
        using (var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var font = new SKFont(SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default, 34))
        {
            var baseline = side + (LabelHeight + 24) / 2f + 8;
            canvas.DrawText(codeLabel, side / 2f, baseline, SKTextAlign.Center, font, textPaint);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
