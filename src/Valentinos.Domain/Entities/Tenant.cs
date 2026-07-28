using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }

    public bool EmailNotificationsEnabled { get; set; } = true;
    public string? NotificationEmails { get; set; }
    public bool WhatsAppNotificationsEnabled { get; set; }
    public bool SmsNotificationsEnabled { get; set; }
    public bool InAppNotificationsEnabled { get; set; }

    public static Tenant Create(string slug, string nombre)
    {
        var normalizado = (slug ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "-");
        if (string.IsNullOrWhiteSpace(normalizado))
            throw new ArgumentException("El slug no puede estar vacío.", nameof(slug));

        return new Tenant { Slug = normalizado, Nombre = nombre };
    }
}
