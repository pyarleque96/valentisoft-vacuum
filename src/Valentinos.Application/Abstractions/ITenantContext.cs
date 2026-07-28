namespace Valentinos.Application.Abstractions;

public interface ITenantContext
{
    Guid? TenantId { get; }
    void Set(Guid tenantId);
}
