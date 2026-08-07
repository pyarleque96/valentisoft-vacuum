using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

// Un "site" (sede) dentro de un tenant. MasterCorp tiene varios sites; cada site
// tiene sus propios vacuums, sus propios empleados, su QR fijo, y sus destinatarios
// de notificación. Se identifica en la URL por su Slug (p. ej. "XjUS3").
public class Site : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;   // código de negocio, p. ej. "069"
    public string Slug { get; set; } = string.Empty;   // segmento de URL aleatorio, p. ej. "XjUS3"
    public string? Emails { get; set; }                 // destinatarios (TO) del reporte del site, separados por coma
}
