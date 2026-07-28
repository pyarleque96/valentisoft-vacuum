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
        var absoluteDir = ResolveWithinRoot(safePrefix, nameof(prefix));
        var ext = extension.TrimStart('.');
        var fileName = $"{Guid.NewGuid():N}.{ext}";
        var relative = $"{safePrefix}/{fileName}";

        Directory.CreateDirectory(absoluteDir);
        var absolutePath = Path.Combine(absoluteDir, fileName);
        await File.WriteAllBytesAsync(absolutePath, content, ct);

        return relative;
    }

    public async Task<byte[]?> GetAsync(string fileKey, CancellationToken ct = default)
    {
        var absolutePath = ResolveWithinRoot(fileKey, nameof(fileKey));
        if (!File.Exists(absolutePath)) return null;
        return await File.ReadAllBytesAsync(absolutePath, ct);
    }

    // Evita path traversal: la ruta resuelta debe quedar bajo _root.
    // Rechaza entradas nulas/vacías, absolutas, con segmentos ".." o que resuelvan
    // fuera de _root (incluyendo directorios "hermanos" que comparten el prefijo
    // de cadena de _root pero no son subdirectorios reales, p. ej. "C:\app\storage.bak").
    private string ResolveWithinRoot(string input, string paramName)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Ruta inválida.", paramName);

        if (Path.IsPathRooted(input))
            throw new ArgumentException("Ruta fuera del almacenamiento.", paramName);

        if (OperatingSystem.IsWindows() && input.Contains(':'))
            throw new ArgumentException("Ruta inválida.", paramName);

        if (input.Contains(".."))
            throw new ArgumentException("Ruta inválida.", paramName);

        var full = Path.GetFullPath(Path.Combine(_root, input));
        if (full != _root && !full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Ruta fuera del almacenamiento.", paramName);

        return full;
    }

    private static string SanitizeSegment(string segment)
    {
        var s = (segment ?? string.Empty).Trim().Replace("\\", "-").Replace("/", "-");
        return string.IsNullOrWhiteSpace(s) ? "general" : s;
    }
}
