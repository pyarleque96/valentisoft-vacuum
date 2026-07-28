using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Application.Reports;
using Valentinos.Application.Storage;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;

namespace Valentinos.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;
    private readonly INotificationService _notifications;

    public ReportService(AppDbContext db, ITenantContext tenant, IFileStorage storage,
        INotificationService notifications)
    {
        _db = db;
        _tenant = tenant;
        _storage = storage;
        _notifications = notifications;
    }

    public static string ExtensionForContentType(string contentType) => contentType switch
    {
        "image/jpeg" or "image/jpg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "bin"
    };

    public async Task<ReportDto> CreateReportAsync(CreateReportRequest req, CancellationToken ct = default)
    {
        // El query filter garantiza que solo se resuelve un activo del tenant actual.
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Codigo == req.AssetCodigo, ct)
            ?? throw new InvalidOperationException("El activo no existe para este tenant.");

        var report = Report.Create(asset.Id, req.Descripcion, req.Severidad, req.Ubicacion, req.ReportadoPor);
        _db.Reports.Add(report);

        var prefix = _tenant.TenantId?.ToString("N") ?? "general";
        foreach (var photo in req.Photos)
        {
            var ext = ExtensionForContentType(photo.ContentType);
            var key = await _storage.SaveAsync(photo.Content, ext, prefix, ct);
            _db.ReportPhotos.Add(new ReportPhoto
            {
                ReportId = report.Id,
                FileKey = key,
                ContentType = photo.ContentType
            });
        }

        await _db.SaveChangesAsync(ct);

        try
        {
            await _notifications.NotifyReportCreatedAsync(new ReportCreatedNotification(
                report.TenantId, asset.Codigo, report.Severidad.ToString(),
                report.Descripcion, report.Ubicacion, report.ReportadoPor), ct);
        }
        catch
        {
            // La creación del reporte no debe fallar si la notificación falla.
        }

        return new ReportDto(
            report.Id, asset.Id, asset.Codigo, report.Descripcion,
            report.Severidad.ToString(), report.Ubicacion, report.ReportadoPor,
            report.Estado.ToString(), req.Photos.Count, report.CreatedAt);
    }
}
