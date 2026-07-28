using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Valentinos.Application.Reports;
using Valentinos.Domain.Enums;

namespace Valentinos.Api.Controllers;

[ApiController]
[Route("api/public/{slug}/reports")]
public class PublicReportsController : ControllerBase
{
    private const int MaxPhotos = 5;
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    private readonly IReportService _reports;
    public PublicReportsController(IReportService reports) => _reports = reports;

    [HttpPost]
    [EnableRateLimiting("public-reports")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Create(
        string slug,
        [FromForm] string codigo,
        [FromForm] string descripcion,
        [FromForm] string severidad,
        [FromForm] string? ubicacion,
        [FromForm] string? reportadoPor,
        [FromForm] IFormFileCollection? fotos,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
            return BadRequest("La descripción es obligatoria.");
        if (!Enum.TryParse<Severidad>(severidad, ignoreCase: true, out var sev))
            return BadRequest("Severidad inválida.");
        if (string.IsNullOrWhiteSpace(codigo))
            return BadRequest("El código del activo es obligatorio.");

        var files = fotos ?? (IFormFileCollection)new FormFileCollection();
        if (files.Count > MaxPhotos)
            return BadRequest($"Máximo {MaxPhotos} fotos por reporte.");

        var photos = new List<ReportPhotoInput>();
        foreach (var f in files)
        {
            if (f.Length <= 0) continue;
            if (f.Length > MaxPhotoBytes)
                return BadRequest($"Cada foto no puede superar {MaxPhotoBytes / (1024 * 1024)} MB.");
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            photos.Add(new ReportPhotoInput(ms.ToArray(), f.ContentType));
        }

        try
        {
            var dto = await _reports.CreateReportAsync(new CreateReportRequest(
                codigo, descripcion, sev, ubicacion, reportadoPor, photos), ct);
            return StatusCode(StatusCodes.Status201Created, dto);
        }
        catch (InvalidOperationException)
        {
            return NotFound("El activo no existe para este tenant.");
        }
    }
}
