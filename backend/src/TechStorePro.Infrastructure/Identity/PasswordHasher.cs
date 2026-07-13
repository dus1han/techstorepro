using System.Security.Cryptography;
using TechStorePro.Application.Common.Interfaces;

namespace TechStorePro.Infrastructure.Identity;

/// <summary>
/// PBKDF2-HMAC-SHA256, 210,000 iterations (the OWASP 2023 figure for this algorithm), with a
/// 128-bit random salt per password.
///
/// Format: {version}.{iterations}.{salt}.{hash}, all base64. The iteration count is stored <em>in
/// the hash</em> rather than read from configuration, so raising it later does not invalidate every
/// existing password — old hashes keep verifying with their own cost, and can be re-hashed on next
/// successful login.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 210_000;
    private const int Version = 1;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashBytes);

        return string.Join('.',
            Version,
            Iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string hash)
    {
        // A malformed stored hash is a data problem, not a caller problem. Returning false rather
        // than throwing keeps the login path uniform: every failure looks and costs the same, so
        // nothing here can be used to distinguish "bad password" from "corrupt row".
        var parts = hash.Split('.');

        if (parts.Length != 4
            || !int.TryParse(parts[0], out var version) || version != Version
            || !int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            return false;
        }

        byte[] salt, expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

        // Constant-time: a byte-by-byte comparison that returns early leaks how much of the hash
        // was correct, which is enough to reconstruct it one byte at a time.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
