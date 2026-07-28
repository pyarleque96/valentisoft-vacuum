namespace Valentinos.Application.Assets;

public record CreateAssetTypeRequest(string Nombre, string Prefijo);
public record CreateAssetRequest(Guid AssetTypeId, string? Ubicacion);

public record AssetTypeDto(Guid Id, string Nombre, string Prefijo, int CorrelativoActual);
public record AssetDto(Guid Id, Guid AssetTypeId, string Codigo, string Estado, string? Ubicacion);
