using Microsoft.Extensions.Logging;
using Valentinos.Application.Notifications;

namespace Valentinos.Infrastructure.Notifications;

// Sender de email para desarrollo/demo: en vez de enviar por SMTP, imprime el
// correo en la consola de la API. Sirve para VER la alerta cuando se crea un
// reporte, sin configurar un servidor de correo real.
public class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;
    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "\n========== 📧 EMAIL (demo) ==========\nPara: {To}\nAsunto: {Subject}\n{Body}\n=====================================",
            string.Join(", ", to), subject, body);
        return Task.CompletedTask;
    }
}
