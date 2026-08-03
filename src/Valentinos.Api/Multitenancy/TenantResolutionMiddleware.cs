using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Multitenancy;

public static class SlugResolver
{
    // Subdominios reservados: NO se tratan como tenant.
    private static readonly string[] Reserved = { "www", "app", "api", "admin" };

    // Extrae el slug de /e/{slug}/... y /api/public/{slug}/...
    public static string? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var segs = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segs.Length >= 2 && segs[0] == "e")
            return segs[1];
        if (segs.Length >= 3 && segs[0] == "api" && segs[1] == "public")
            return segs[2];
        return null;
    }

    // Extrae el slug del subdominio: mastercorp.valentisoft.com -> "mastercorp".
    // Devuelve null en el apex, subdominios reservados, localhost o cualquier host
    // que no cuelgue de baseDomain (p. ej. el túnel *.trycloudflare.com).
    public static string? FromHost(string? host, string? baseDomain)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(baseDomain)) return null;
        host = host.Split(':')[0].Trim().ToLowerInvariant();      // sin puerto
        baseDomain = baseDomain.Trim().ToLowerInvariant();
        if (host == baseDomain) return null;                      // apex
        var suffix = "." + baseDomain;
        if (!host.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var label = host[..^suffix.Length].Split('.')[0];         // etiqueta más a la izquierda
        if (label.Length == 0 || Reserved.Contains(label)) return null;
        return label;
    }

    // Inyecta el slug en la posición que esperan las rutas del controller, para que
    // las URLs "cortas" del subdominio (/reports, /f, /qr, /e/VAC-007, /api/public/...)
    // matcheen las rutas /reports/{slug}, etc. Idempotente: si el slug ya está en su
    // sitio (p. ej. llamadas internas que ya lo incluyen), no lo duplica.
    public static string? InjectSlug(string? path, string slug)
    {
        var raw = string.IsNullOrEmpty(path) ? "/" : path;
        var segs = raw.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segs.Count == 0) return null;

        int idx;
        if (segs[0] is "reports" or "f" or "qr" or "e" or "demo") idx = 1;
        else if (segs.Count >= 2 && segs[0] == "api" && segs[1] == "public") idx = 2;
        else return null;

        if (segs.Count > idx && string.Equals(segs[idx], slug, StringComparison.OrdinalIgnoreCase))
            return null; // ya lo tiene

        // /f/hk es una ruta literal del QR fijo (el tenant viene del subdominio);
        // no se le inyecta el slug.
        if (segs[0] == "f" && segs.Count > idx && string.Equals(segs[idx], "hk", StringComparison.OrdinalIgnoreCase))
            return null;

        segs.Insert(idx, slug);
        return "/" + string.Join('/', segs);
    }
}

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _baseDomain;

    public TenantResolutionMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _baseDomain = config["Tenancy:BaseDomain"]; // p. ej. "valentisoft.com"; vacío en local
    }

    public async Task InvokeAsync(HttpContext ctx, ITenantContext tenantContext, AppDbContext db)
    {
        // 1) Tenant por subdominio (producción). Reescribe la ruta para que las URLs
        //    cortas del subdominio matcheen las rutas actuales /{recurso}/{slug}.
        var subSlug = SlugResolver.FromHost(ctx.Request.Host.Host, _baseDomain);
        if (subSlug is not null)
        {
            var injected = SlugResolver.InjectSlug(ctx.Request.Path.Value, subSlug);
            if (injected is not null) ctx.Request.Path = injected;
        }

        // 2) Fijar el tenant: por subdominio si aplica; si no, por la ruta (comportamiento local).
        var slug = subSlug ?? SlugResolver.FromPath(ctx.Request.Path.Value ?? string.Empty);
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
