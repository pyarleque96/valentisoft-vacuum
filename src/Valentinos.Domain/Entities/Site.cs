using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

// Un "site" (sede) dentro de un tenant. MasterCorp tiene varios sites; cada site
// tiene sus propios vacuums, su QR fijo, y sus encargados (que reciben copia del
// reporte). Se identifica en la URL por su Slug (p. ej. "site-XjUS3").
public class Site : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;   // código de negocio, p. ej. "069"
    public string Slug { get; set; } = string.Empty;   // segmento de URL aleatorio, p. ej. "XjUS3"
    public string? CcEmails { get; set; }               // encargados (CC del reporte), separados por coma
}
