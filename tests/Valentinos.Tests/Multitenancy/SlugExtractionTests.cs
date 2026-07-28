using Valentinos.Api.Multitenancy;
using Xunit;

namespace Valentinos.Tests.Multitenancy;

public class SlugExtractionTests
{
    [Theory]
    [InlineData("/r/mastercorp/VAC-001", "mastercorp")]
    [InlineData("/api/public/mastercorp/reports", "mastercorp")]
    [InlineData("/api/public/master-corp/reports", "master-corp")]
    [InlineData("/admin/dashboard", null)]
    [InlineData("/", null)]
    [InlineData("/r/", null)]
    public void FromPath_ExtraeSlugCorrecto(string path, string? esperado)
    {
        Assert.Equal(esperado, SlugResolver.FromPath(path));
    }
}
