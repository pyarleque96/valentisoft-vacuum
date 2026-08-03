using System.Security.Cryptography;

namespace Valentinos.Infrastructure.Auth;

// Hash de contraseñas con PBKDF2 (SHA256, 100k iteraciones). Formato almacenado:
// "pbkdf2$<iter>$<saltB64>$<hashB64>". Sin dependencias externas.
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;   // bytes
    private const int HashSize = 32;   // bytes

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iter)) return false;

        byte[] salt, expected;
        try { salt = Convert.FromBase64String(parts[2]); expected = Convert.FromBase64String(parts[3]); }
        catch { return false; }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
