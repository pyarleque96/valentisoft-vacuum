using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Assets;
using Valentinos.Domain.Entities;
using Valentinos.Domain.Enums;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure.Assets;

public class AssetService : IAssetService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public AssetService(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public static string FormatCodigo(string prefijo, int correlativo)
        => $"{prefijo}-{correlativo.ToString("D3")}";

    public async Task<AssetTypeDto> CreateAssetTypeAsync(CreateAssetTypeRequest req, CancellationToken ct = default)
    {
        var tipo = AssetType.Create(req.Nombre, req.Prefijo);
        _db.AssetTypes.Add(tipo);
        await _db.SaveChangesAsync(ct);
        return ToDto(tipo);
    }

    public async Task<IReadOnlyList<AssetTypeDto>> ListAssetTypesAsync(CancellationToken ct = default)
        => await _db.AssetTypes.OrderBy(t => t.Prefijo)
            .Select(t => ToDto(t)).ToListAsync(ct);

    public async Task<AssetDto> CreateAssetAsync(CreateAssetRequest req, CancellationToken ct = default)
    {
        // El global query filter garantiza que solo se resuelve un tipo del tenant actual.
        var tipo = await _db.AssetTypes.FirstOrDefaultAsync(t => t.Id == req.AssetTypeId, ct)
            ?? throw new InvalidOperationException("El tipo de activo no existe para este tenant.");

        // Asignación atómica del correlativo: se incrementa el contador del tipo y se
        // persiste junto con el nuevo activo en un solo SaveChanges (misma transacción).
        tipo.CorrelativoActual += 1;
        var codigo = FormatCodigo(tipo.Prefijo, tipo.CorrelativoActual);

        var asset = new Asset
        {
            AssetTypeId = tipo.Id,
            Codigo = codigo,
            Estado = AssetEstado.Activo,
            Ubicacion = req.Ubicacion
        };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return ToDto(asset);
    }

    public async Task<IReadOnlyList<AssetDto>> ListAssetsAsync(CancellationToken ct = default)
        => await _db.Assets.OrderBy(a => a.CreatedAt).ThenBy(a => a.Codigo)
            .Select(a => ToDto(a)).ToListAsync(ct);

    private static AssetTypeDto ToDto(AssetType t)
        => new(t.Id, t.Nombre, t.Prefijo, t.CorrelativoActual);

    private static AssetDto ToDto(Asset a)
        => new(a.Id, a.AssetTypeId, a.Codigo, a.Estado.ToString(), a.Ubicacion);
}
