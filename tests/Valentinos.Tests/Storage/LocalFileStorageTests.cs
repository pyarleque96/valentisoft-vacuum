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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
