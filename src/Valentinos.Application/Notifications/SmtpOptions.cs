namespace Valentinos.Application.Notifications;

public class SmtpOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "no-reply@valentinos.com";
    public string FromName { get; set; } = "Valentino's";
    public string? Cc { get; set; } // copia (CC), separada por comas
}
