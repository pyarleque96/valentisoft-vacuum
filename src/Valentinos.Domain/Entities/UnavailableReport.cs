using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

// Reporte de "equipo no disponible" (p. ej. no hay aspiradora disponible en el cuarto
// de housekeeping). NO está ligado a un activo específico: se reporta desde un QR fijo,
// eligiendo el tipo de equipo. Alimenta una sección propia en la página de KPIs.
public class UnavailableReport : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }        // site donde se reportó
    public string EmployeeName { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = "Vacuum";
    public string? Nota { get; set; }
}
