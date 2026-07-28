using Valentinos.Application.Abstractions;

namespace Valentinos.Api.Multitenancy;

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public void Set(Guid tenantId) => TenantId = tenantId;
}
