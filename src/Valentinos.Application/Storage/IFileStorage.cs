namespace Valentinos.Application.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(byte[] content, string extension, string prefix, CancellationToken ct = default);
    Task<byte[]?> GetAsync(string fileKey, CancellationToken ct = default);
}
