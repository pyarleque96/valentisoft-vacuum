using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Multitenancy;

public static class SlugResolver
{
    // Subdominios reservados: NO se tratan como tenant.
    private static readonly string[] Reserved = { "www", "app", "api", "admin" };

    // Extrae el slug del tenant desde el subdominio: mastercorp.valentisoft.com -> "mastercorp".
    // Devuelve null en el apex, subdominios reservados, localhost o cualquier host que no
    // cuelgue de baseDomain (p. ej. el túnel *.trycloudflare.com).
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
}

// Resuelve el TENANT desde el subdominio y lo fija en el ITenantContext. Las URLs ya
// llevan el site slug en el path (/{siteSlug}/e/{code}); los controllers resuelven el
// site. El tenant en contexto es necesario para el write-path (stamping fail-closed).
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
        var subSlug = SlugResolver.FromHost(ctx.Request.Host.Host, _baseDomain);
        if (subSlug is not null)
        {
            var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == subSlug);
            if (tenant is not null) tenantContext.Set(tenant.Id);
        }
        await _next(ctx);
    }
}
