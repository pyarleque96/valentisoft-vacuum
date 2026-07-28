using SkiaSharp;
using Valentinos.Application.Qr;

namespace Valentinos.Infrastructure.Qr;

public class SkiaQrSheetRenderer : IQrSheetRenderer
{
    // A4 a 72 dpi aprox.
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 36f;
    private const int Columns = 3;
    private const int RowsPerPage = 4;

    private readonly IQrRenderer _qr;

    public SkiaQrSheetRenderer(IQrRenderer qr) => _qr = qr;

    public byte[] RenderPdf(IReadOnlyList<QrSheetItem> items)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            var perPage = Columns * RowsPerPage;
            var cellW = (PageWidth - 2 * Margin) / Columns;
            var cellH = (PageHeight - 2 * Margin) / RowsPerPage;

            // Siempre emitir al menos una página (aunque no haya ítems) para un PDF válido.
            var pages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)perPage));

            for (var page = 0; page < pages; page++)
            {
                var canvas = doc.BeginPage(PageWidth, PageHeight);
                for (var i = 0; i < perPage; i++)
                {
                    var index = page * perPage + i;
                    if (index >= items.Count) break;

                    var item = items[index];
                    var png = _qr.RenderPng(new QrRenderRequest(item.Slug, item.Codigo, item.LogoPng));
                    using var bmp = SKBitmap.Decode(png);
                    if (bmp is null) continue;

                    var col = i % Columns;
                    var rowIdx = i / Columns;
                    var x = Margin + col * cellW;
                    var y = Margin + rowIdx * cellH;

                    // Encajar el QR (cuadrado + franja) dentro de la celda, con padding.
                    var padding = 8f;
                    var maxW = cellW - 2 * padding;
                    var maxH = cellH - 2 * padding;
                    var scale = Math.Min(maxW / bmp.Width, maxH / bmp.Height);
                    var drawW = bmp.Width * scale;
                    var drawH = bmp.Height * scale;
                    var dx = x + (cellW - drawW) / 2f;
                    var dy = y + (cellH - drawH) / 2f;
                    canvas.DrawBitmap(bmp, new SKRect(dx, dy, dx + drawW, dy + drawH),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                }
                doc.EndPage();
            }
            doc.Close();
        }
        return ms.ToArray();
    }
}
