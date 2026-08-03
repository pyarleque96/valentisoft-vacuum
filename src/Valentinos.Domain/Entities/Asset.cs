using Valentinos.Domain.Common;
using Valentinos.Domain.Enums;

namespace Valentinos.Domain.Entities;

public class Asset : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }        // site al que pertenece el vacuum
    public Guid AssetTypeId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public AssetEstado Estado { get; set; } = AssetEstado.Activo;
    public string? Ubicacion { get; set; }
}
