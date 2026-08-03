using SkiaSharp;

namespace Valentinos.Api.Qr;

// Hoja imprimible con todos los QRs de los equipos, en grilla (por defecto 12 por página).
// Recibe los PNG ya renderizados (cada uno con su código impreso y logo al centro).
public static class QrSheetPdf
{
    private const float W = 595f, Hgt = 842f, M = 36f; // A4 ~72dpi

    public static byte[] Render(IReadOnlyList<byte[]> qrPngs, int perPage = 6)
    {
        perPage = perPage < 1 ? 6 : perPage;
        const int cols = 2; // 2 columnas -> con perPage=6 quedan 2x3 (QRs más grandes)
        var rows = Math.Max(1, (int)Math.Ceiling(perPage / (double)cols));
        var cellW = (W - 2 * M) / cols;
        var cellH = (Hgt - 2 * M) / rows;
        const float pad = 8f;

        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            var pages = Math.Max(1, (int)Math.Ceiling(qrPngs.Count / (double)perPage));
            for (var page = 0; page < pages; page++)
            {
                var canvas = doc.BeginPage(W, Hgt);
                for (var i = 0; i < perPage; i++)
                {
                    var index = page * perPage + i;
                    if (index >= qrPngs.Count) break;

                    using var bmp = SKBitmap.Decode(qrPngs[index]);
                    if (bmp is null) continue;

                    var col = i % cols;
                    var row = i / cols;
                    var x = M + col * cellW;
                    var y = M + row * cellH;

                    var maxW = cellW - 2 * pad;
                    var maxH = cellH - 2 * pad;
                    var scale = Math.Min(maxW / bmp.Width, maxH / bmp.Height);
                    var dw = bmp.Width * scale;
                    var dh = bmp.Height * scale;
                    var dx = x + (cellW - dw) / 2f;
                    var dy = y + (cellH - dh) / 2f;
                    canvas.DrawBitmap(bmp, new SKRect(dx, dy, dx + dw, dy + dh),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                }
                doc.EndPage();
            }
            doc.Close();
        }
        return ms.ToArray();
    }
}
