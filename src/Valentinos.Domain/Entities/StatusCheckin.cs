using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

// Registro de un check-in de estado de un activo (operativo / con fallas / fuera de
// servicio). Alimenta la página de KPIs. Usado por la demo.
public class StatusCheckin : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }        // site donde se hizo el check-in
    public string EmployeeName { get; set; } = string.Empty;
    public string AssetCodigo { get; set; } = string.Empty;
    public string EstadoKey { get; set; } = "operational"; // operational | AMedias | NoFunciona
    public string? Nota { get; set; }
}
