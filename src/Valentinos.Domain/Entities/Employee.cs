using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class Employee : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }    // site al que pertenece (el autocompletar filtra por él)
    public string Nombre { get; set; } = string.Empty;
}
