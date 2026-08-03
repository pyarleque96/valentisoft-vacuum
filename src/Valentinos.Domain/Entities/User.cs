using Valentinos.Domain.Common;

namespace Valentinos.Domain.Entities;

// Usuario del panel de administración, ligado a un tenant. La contraseña se guarda
// SIEMPRE hasheada (PasswordHash), nunca en texto plano.
public class User : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "admin";        // por ahora solo "admin" (a nivel tenant)
    public string DisplayName { get; set; } = string.Empty;
}
