namespace Valentinos.Api.Reports;

// Dispara el envío DIARIO de reportes todos los días a las 10:00 (hora local del
// servidor), delegando en DailyReportRunner. Sin dependencias externas.
// NOTA: requiere que la app esté corriendo a esa hora (idealmente instalada como servicio).
public class DailyReportScheduler : BackgroundService
{
    private readonly TimeSpan _sendAt; // hora local de envío (config Reports:DailyTime, HH:mm)
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DailyReportScheduler> _logger;

    public DailyReportScheduler(IServiceScopeFactory scopes, ILogger<DailyReportScheduler> logger, IConfiguration config)
    {
        _scopes = scopes;
        _logger = logger;
        _sendAt = TimeSpan.TryParse(config["Reports:DailyTime"], out var t) ? t : new TimeSpan(10, 0, 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var next = now.Date.Add(_sendAt);
            if (next <= now) next = next.AddDays(1);
            _logger.LogInformation("⏰ Próximo reporte diario programado para {Next:yyyy-MM-dd HH:mm}", next);

            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { break; }

            await SendDailyForAllTenantsAsync(stoppingToken);
        }
    }

    private async Task SendDailyForAllTenantsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<DailyReportRunner>();
            await runner.RunAsync(ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo general en el scheduler de reporte diario");
        }
    }
}
