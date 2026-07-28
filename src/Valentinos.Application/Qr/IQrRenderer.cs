namespace Valentinos.Application.Qr;

public record QrRenderRequest(string Slug, string Codigo, byte[]? LogoPng);

public interface IQrRenderer
{
    byte[] RenderPng(QrRenderRequest req);
    string BuildUrl(string slug, string codigo);
}
