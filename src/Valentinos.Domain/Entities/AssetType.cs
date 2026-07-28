using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class AssetType : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Prefijo { get; set; } = string.Empty;
    public int CorrelativoActual { get; set; }

    public static AssetType Create(string nombre, string prefijo)
    {
        var normalizado = (prefijo ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(normalizado))
            throw new ArgumentException("El prefijo no puede estar vacío.", nameof(prefijo));

        return new AssetType { Nombre = nombre, Prefijo = normalizado, CorrelativoActual = 0 };
    }
}
