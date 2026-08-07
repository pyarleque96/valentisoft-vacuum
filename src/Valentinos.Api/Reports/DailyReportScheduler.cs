namespace Valentinos.Api.Reports;

// Dispara el envío DIARIO de reportes todos los días a las 10:00 (hora local del
// servidor), delegando en DailyReportRunner. Sin dependencias externas.
// NOTA: requiere que la app esté corriendo a esa hora (idealmente instalada como servicio).
//
// Implementado como POLLING (cada 1 minuto) en vez de un único Task.Delay largo hasta
// la hora objetivo: Task.Delay mide tiempo transcurrido con el contador de ticks del
// SO, no reloj de pared, así que tras una suspensión/reanudación de la máquina el delay
// expira en un momento que ya no corresponde a las 10:00 y el código anterior enviaba
// igual sin volver a mirar el reloj. Con polling, cada minuto se vuelve a evaluar
// DateTime.Now real, así que una suspensión solo retrasa el próximo chequeo, nunca
// dispara un envío a destiempo ni lo duplica.
public class DailyReportScheduler : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly TimeSpan _sendAt; // hora local de envío (config Reports:DailyTime, HH:mm)
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DailyReportScheduler> _logger;
    private DateOnly? _lastSentDate;

    public DailyReportScheduler(IServiceScopeFactory scopes, ILogger<DailyReportScheduler> logger, IConfiguration config)
    {
        _scopes = scopes;
        _logger = logger;
        _sendAt = TimeSpan.TryParse(config["Reports:DailyTime"], out var t) ? t : new TimeSpan(10, 0, 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogNextFire(DateTime.Now);
        DateTime? lastPoll = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;

            // Detección de salto de reloj: si entre dos chequeos pasó bastante más que el
            // intervalo de polling, la máquina estuvo suspendida (o el reloj cambió). Este
            // es el log que prueba cuándo pasó.
            if (lastPoll is { } prev)
            {
                var gap = now - prev;
                if (gap > PollInterval + PollInterval)
                {
                    _logger.LogWarning(
                        "⚠️ Salto de reloj detectado: chequeo anterior {Prev:yyyy-MM-dd HH:mm:ss}, " +
                        "chequeo actual {Now:yyyy-MM-dd HH:mm:ss} (salto de {Gap}). " +
                        "Probable suspensión/reanudación de la máquina.",
                        prev, now, gap);
                }
            }
            lastPoll = now;

            if (DailyReportSchedule.ShouldSend(now, _sendAt, _lastSentDate))
            {
                var lateness = DailyReportSchedule.Lateness(now, _sendAt);
                if (lateness > TimeSpan.FromMinutes(1))
                {
                    _logger.LogWarning(
                        "⏰ Envío del reporte diario FUERA DE HORA: ahora {Now:yyyy-MM-dd HH:mm:ss}, " +
                        "objetivo {SendAt}, {Lateness} tarde.", now, _sendAt, lateness);
                }
                else
                {
                    _logger.LogInformation(
                        "⏰ Disparando reporte diario a tiempo: ahora {Now:yyyy-MM-dd HH:mm:ss}, " +
                        "objetivo {SendAt} ({Lateness} de diferencia).", now, _sendAt, lateness);
                }

                try
                {
                    await SendDailyForAllTenantsAsync(stoppingToken);
                }
                finally
                {
                    // Se marca como enviado hoy AUNQUE el envío haya fallado: si no, un
                    // error deja el flag sin marcar y el próximo poll (1 minuto después)
                    // reintenta sin parar durante el resto del día.
                    _lastSentDate = DateOnly.FromDateTime(now);
                    LogNextFire(now);
                }
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void LogNextFire(DateTime from)
    {
        var next = from.Date.Add(_sendAt);
        if (next <= from) next = next.AddDays(1);
        _logger.LogInformation("⏰ Próximo reporte diario programado para {Next:yyyy-MM-dd HH:mm:ss}", next);
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
