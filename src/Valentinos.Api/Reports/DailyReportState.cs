using Microsoft.Extensions.Logging;

namespace Valentinos.Api.Reports;

// Persiste en disco la fecha del último envío diario, para que DailyReportScheduler
// no la pierda al reiniciar el proceso. Sin esto, un reinicio a las 10:05 (dentro de
// la ventana de gracia, con el campo en memoria vacío) dispara un envío duplicado.
public class DailyReportState
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly string _path;
    private readonly ILogger<DailyReportState> _logger;

    public DailyReportState(string path, ILogger<DailyReportState> logger)
    {
        _path = path;
        _logger = logger;
    }

    // Devuelve null si el archivo no existe, está vacío o su contenido no se puede
    // interpretar como fecha: nunca lanza. Un archivo de estado corrupto no debe
    // tumbar el arranque de la app, solo hacer que se trate como "nunca se envió".
    public DateOnly? ReadLastSent()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var text = File.ReadAllText(_path).Trim();
            if (text.Length == 0) return null;
            return DateOnly.TryParseExact(text, DateFormat, out var date) ? date : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ No se pudo leer el estado del reporte diario ({Path}); se trata como 'nunca enviado'.", _path);
            return null;
        }
    }

    // No relanza en caso de fallo de IO: no poder registrar la fecha arriesga un
    // duplicado (se reintentará el envío), pero tirar abajo el BackgroundService por
    // un error de disco sería peor (deja de enviar reportes por completo). Se prefiere
    // el riesgo de duplicado, logueado, a la caída del scheduler.
    public void WriteLastSent(DateOnly date)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, date.ToString(DateFormat));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ No se pudo guardar el estado del reporte diario ({Path}); riesgo de reenvío si la app se reinicia dentro de la ventana de gracia.", _path);
        }
    }
}
