using System.Security.Cryptography;

namespace SaasEngine.Api.Infrastructure.Security;

/// <summary>
/// Provides secure, AOT-compatible password hashing using PBKDF2 with SHA-256.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16; // 128-bit
    private const int KeySize = 32;  // 256-bit
    private const int Iterations = 100000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Hashes the specified plain text password.
    /// </summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithm, KeySize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies the plain text password against the hashed password.
    /// </summary>
    public static bool Verify(string password, string hashedPassword)
    {
        var parts = hashedPassword.Split('.', 3);
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var hash = Convert.FromBase64String(parts[2]);

        var testHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithm, KeySize);

        return CryptographicOperations.FixedTimeEquals(hash, testHash);
    }
}
