namespace Valentinos.Application.Notifications;

public record ReportCreatedNotification(
    Guid TenantId,
    string AssetCodigo,
    string Severidad,
    string Descripcion,
    string? Ubicacion,
    string? ReportadoPor);
