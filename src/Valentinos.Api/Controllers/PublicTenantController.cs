using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Api.Controllers;

[ApiController]
[Route("api/public/{slug}")]
public class PublicTenantController : ControllerBase
{
    private readonly AppDbContext _db;
    public PublicTenantController(AppDbContext db) => _db = db;

    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI(string slug)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();
        return Ok(new { tenantId = tenant.Id, slug = tenant.Slug, nombre = tenant.Nombre });
    }

    [HttpGet("assets/{codigo}")]
    public async Task<IActionResult> GetAsset(string slug, string codigo)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null) return NotFound();

        var asset = await _db.Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.Id && a.Codigo == codigo);
        if (asset is null) return NotFound();

        var tipo = await _db.AssetTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == asset.AssetTypeId);

        return Ok(new { codigo = asset.Codigo, tipoNombre = tipo?.Nombre });
    }
}
