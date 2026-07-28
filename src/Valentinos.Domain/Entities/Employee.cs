using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class Employee : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Nombre { get; set; } = string.Empty;
}
