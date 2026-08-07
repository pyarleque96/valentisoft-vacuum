namespace Valentinos.Api.Reports;

// Decide si toca enviar el reporte diario. Aislado del BackgroundService para
// poder probarlo sin temporizadores ni relojes reales.
public static class DailyReportSchedule
{
    // Devuelve true si en `now` corresponde enviar: ya pasó la hora objetivo de hoy
    // y todavía no se envió hoy. `lastSentDate` es el último día en que se envió.
    public static bool ShouldSend(DateTime now, TimeSpan sendAt, DateOnly? lastSentDate)
        => now.TimeOfDay >= sendAt && lastSentDate != DateOnly.FromDateTime(now);

    // Cuánto tarde va el envío respecto de la hora objetivo de ese día.
    public static TimeSpan Lateness(DateTime now, TimeSpan sendAt)
        => now.TimeOfDay - sendAt;
}
