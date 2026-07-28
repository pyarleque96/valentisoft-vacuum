using Valentinos.Application.Storage;

namespace Valentinos.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(FileStorageOptions options)
    {
        _root = Path.GetFullPath(options.RootPath);
    }

    public async Task<string> SaveAsync(byte[] content, string extension, string prefix, CancellationToken ct = default)
    {
        var safePrefix = SanitizeSegment(prefix);
        var ext = extension.TrimStart('.');
        var fileName = $"{Guid.NewGuid():N}.{ext}";
        var relative = $"{safePrefix}/{fileName}";

        var absoluteDir = Path.Combine(_root, safePrefix);
        Directory.CreateDirectory(absoluteDir);
        var absolutePath = Path.Combine(absoluteDir, fileName);
        await File.WriteAllBytesAsync(absolutePath, content, ct);

        return relative;
    }

    public async Task<byte[]?> GetAsync(string fileKey, CancellationToken ct = default)
    {
        var absolutePath = ResolveWithinRoot(fileKey);
        if (!File.Exists(absolutePath)) return null;
        return await File.ReadAllBytesAsync(absolutePath, ct);
    }

    // Evita path traversal: la ruta resuelta debe quedar bajo _root.
    private string ResolveWithinRoot(string fileKey)
    {
        if (string.IsNullOrWhiteSpace(fileKey) || fileKey.Contains(".."))
            throw new ArgumentException("FileKey inválido.", nameof(fileKey));

        var combined = Path.GetFullPath(Path.Combine(_root, fileKey));
        if (!combined.StartsWith(_root, StringComparison.Ordinal))
            throw new ArgumentException("FileKey fuera del almacenamiento.", nameof(fileKey));
        return combined;
    }

    private static string SanitizeSegment(string segment)
    {
        var s = (segment ?? string.Empty).Trim().Replace("\\", "-").Replace("/", "-");
        return string.IsNullOrWhiteSpace(s) ? "general" : s;
    }
}
