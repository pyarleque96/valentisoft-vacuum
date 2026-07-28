using Valentinos.Domain.Enums;

namespace Valentinos.Application.Reports;

public record ReportPhotoInput(byte[] Content, string ContentType);

public record CreateReportRequest(
    string AssetCodigo,
    string Descripcion,
    Severidad Severidad,
    string? Ubicacion,
    string? ReportadoPor,
    IReadOnlyList<ReportPhotoInput> Photos);

public record ReportDto(
    Guid Id,
    Guid AssetId,
    string AssetCodigo,
    string Descripcion,
    string Severidad,
    string? Ubicacion,
    string? ReportadoPor,
    string Estado,
    int PhotoCount,
    DateTime CreatedAt);
