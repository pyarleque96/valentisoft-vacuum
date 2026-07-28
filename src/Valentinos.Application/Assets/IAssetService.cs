namespace Valentinos.Application.Assets;

public interface IAssetService
{
    Task<AssetTypeDto> CreateAssetTypeAsync(CreateAssetTypeRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<AssetTypeDto>> ListAssetTypesAsync(CancellationToken ct = default);
    Task<AssetDto> CreateAssetAsync(CreateAssetRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<AssetDto>> ListAssetsAsync(CancellationToken ct = default);
}
