namespace Valentinos.Application.Qr;

public record QrRenderRequest(string Slug, string Codigo, byte[]? LogoPng);

public interface IQrRenderer
{
    byte[] RenderPng(QrRenderRequest req);
    string BuildUrl(string slug, string codigo);

    // Genera el QR codificando una URL absoluta arbitraria (p. ej. la del túnel),
    // en lugar de construirla desde QrOptions.BaseUrl.
    byte[] RenderPngForUrl(string url, string codeLabel, byte[]? logoPng);
}
