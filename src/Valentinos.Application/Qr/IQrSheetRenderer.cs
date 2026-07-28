namespace Valentinos.Application.Qr;

public record QrSheetItem(string Slug, string Codigo, byte[]? LogoPng);

public interface IQrSheetRenderer
{
    byte[] RenderPdf(IReadOnlyList<QrSheetItem> items);
}
