using System.Text;
using Valentinos.Application.Storage;
using Valentinos.Infrastructure.Storage;
using Xunit;

namespace Valentinos.Tests.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "valentinos-fs-" + Guid.NewGuid());

    private LocalFileStorage Build() => new(new FileStorageOptions { RootPath = _root });

    [Fact]
    public async Task SaveAsync_LuegoGetAsync_DevuelveElMismoContenido()
    {
        var fs = Build();
        var bytes = Encoding.UTF8.GetBytes("hola");

        var key = await fs.SaveAsync(bytes, "jpg", "mastercorp");

        Assert.StartsWith("mastercorp/", key.Replace('\\', '/'));
        Assert.EndsWith(".jpg", key);

        var back = await fs.GetAsync(key);
        Assert.NotNull(back);
        Assert.Equal(bytes, back);
    }

    [Fact]
    public async Task GetAsync_ClaveInexistente_DevuelveNull()
    {
        var fs = Build();
        Assert.Null(await fs.GetAsync("mastercorp/no-existe.jpg"));
    }

    [Fact]
    public async Task GetAsync_ConPathTraversal_LanzaExcepcion()
    {
        var fs = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => fs.GetAsync("../secreto.txt"));
    }

    [Fact]
    public async Task GetAsync_ConFileKeyAbsoluto_LanzaExcepcion()
    {
        var fs = Build();
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(outside, "secreto");
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => fs.GetAsync(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task GetAsync_ConPrefijoHermanoQueComparteCadena_LanzaExcepcion()
    {
        var fs = Build();
        // Comparte el prefijo de cadena de _root (p. ej. "...storage" vs "...storage.bak")
        // pero es un directorio hermano distinto, no un subdirectorio real.
        var siblingKey = _root + ".bak" + Path.DirectorySeparatorChar + "secret.txt";
        await Assert.ThrowsAsync<ArgumentException>(() => fs.GetAsync(siblingKey));
    }

    [Fact]
    public async Task SaveAsync_ConPrefijoDosPuntos_LanzaExcepcion()
    {
        var fs = Build();
        var bytes = Encoding.UTF8.GetBytes("hola");

        await Assert.ThrowsAsync<ArgumentException>(() => fs.SaveAsync(bytes, "jpg", ".."));

        // El directorio raíz de este test no debe haberse creado ni contener nada:
        // la excepción debe lanzarse antes de tocar el sistema de archivos.
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
