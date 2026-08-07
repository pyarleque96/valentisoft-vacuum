namespace Valentinos.Api.Reports;

// Decide si toca enviar el reporte diario. Aislado del BackgroundService para
// poder probarlo sin temporizadores ni relojes reales.
public static class DailyReportSchedule
{
    // Devuelve true si en `now` corresponde enviar: estamos dentro de la ventana de
    // gracia [sendAt, sendAt + grace) y todavía no se envió hoy. `lastSentDate` es el
    // último día en que se envió.
    //
    // La ventana de gracia es la decisión explícita del usuario: si la máquina estuvo
    // apagada/suspendida durante toda la ventana de las 10:00, ese día se pierde y se
    // espera al siguiente, en vez de mandar el correo a una hora rara. `lastSentDate`
    // sigue siendo necesario aparte: sin él, un reinicio a las 10:05 (dentro de la
    // ventana, con el campo en memoria vacío) mandaría un duplicado.
    public static bool ShouldSend(DateTime now, TimeSpan sendAt, TimeSpan grace, DateOnly? lastSentDate)
        => now.TimeOfDay >= sendAt && now.TimeOfDay < sendAt + grace
           && lastSentDate != DateOnly.FromDateTime(now);

    // Cuánto tarde va el envío respecto de la hora objetivo de ese día.
    public static TimeSpan Lateness(DateTime now, TimeSpan sendAt)
        => now.TimeOfDay - sendAt;
}
