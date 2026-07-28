using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class ReportPhoto : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ReportId { get; set; }
    public string FileKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}
