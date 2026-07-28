namespace Valentinos.Application.Reports;

public interface IReportService
{
    Task<ReportDto> CreateReportAsync(CreateReportRequest req, CancellationToken ct = default);
}
