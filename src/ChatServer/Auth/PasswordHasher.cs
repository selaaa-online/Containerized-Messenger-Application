using System.Security.Cryptography;

namespace ChatServer.Auth;

/// <summary>
/// Hashes and verifies passwords using PBKDF2 (SHA-256) with a random per-user salt.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;      // 128-bit salt
    private const int KeySize = 32;       // 256-bit derived key
    private const int Iterations = 100_000;

    public static (string hash, string salt) Hash(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Derive(password, saltBytes);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var expected = Convert.FromBase64String(hash);
        var actual = Derive(password, saltBytes);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
