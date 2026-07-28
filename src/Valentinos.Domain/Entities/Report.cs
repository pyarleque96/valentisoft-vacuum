using Valentinos.Domain.Common;
using Valentinos.Domain.Enums;

namespace Valentinos.Domain.Entities;

public class Report : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AssetId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public Severidad Severidad { get; set; }
    public string? Ubicacion { get; set; }
    public string? ReportadoPor { get; set; }
    public ReportStatus Estado { get; set; } = ReportStatus.Nuevo;
    public string? Notas { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public static Report Create(Guid assetId, string descripcion, Severidad severidad,
        string? ubicacion, string? reportadoPor)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new ArgumentException("La descripción no puede estar vacía.", nameof(descripcion));

        return new Report
        {
            AssetId = assetId,
            Descripcion = descripcion.Trim(),
            Severidad = severidad,
            Ubicacion = ubicacion,
            ReportadoPor = reportadoPor,
            Estado = ReportStatus.Nuevo
        };
    }
}
