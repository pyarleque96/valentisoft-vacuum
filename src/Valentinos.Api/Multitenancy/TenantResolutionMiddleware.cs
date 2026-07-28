using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Multitenancy;

public static class SlugResolver
{
    // Extrae el slug de /r/{slug}/... y /api/public/{slug}/...
    public static string? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var segs = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segs.Length >= 2 && segs[0] == "r")
            return segs[1];
        if (segs.Length >= 3 && segs[0] == "api" && segs[1] == "public")
            return segs[2];
        return null;
    }
}

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenantContext, AppDbContext db)
    {
        var slug = SlugResolver.FromPath(ctx.Request.Path.Value ?? string.Empty);
        if (slug is not null)
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Slug == slug);
            if (tenant is not null)
                tenantContext.Set(tenant.Id);
        }
        await _next(ctx);
    }
}
