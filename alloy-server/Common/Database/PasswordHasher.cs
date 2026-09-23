using System;
using System.Security.Cryptography;
using System.Text;

namespace Common.Database;

// Password hashing. New hashes are PBKDF2-SHA256 (Iterations rounds, the account's existing random salt), stored as
// "pbkdf2$<iterations>$<base64>". Old rows hold Base64(SHA1(password + salt)) with no prefix: Verify still accepts those, and
// NeedsUpgrade tells the login path to re-hash the password with PBKDF2 on the next successful login - so every account migrates
// silently, nothing is reset. 2026-09-21 audit (C7 / F42): SHA1 is fast enough to brute-force a leaked table in hours.
public static class PasswordHasher {
    public const string Prefix = "pbkdf2";
    public const int Iterations = 100_000;
    private const int HashBytes = 32;

    public static string Hash(string password, string salt) {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? string.Empty), Encoding.UTF8.GetBytes(salt ?? string.Empty),
            Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(bytes)}";
    }

    public static bool IsLegacy(string storedHash) => storedHash == null || !storedHash.StartsWith(Prefix + "$", StringComparison.Ordinal);

    public static bool NeedsUpgrade(string storedHash) {
        if (IsLegacy(storedHash))
            return true;
        var parts = storedHash.Split('$');
        return parts.Length != 3 || !int.TryParse(parts[1], out var iterations) || iterations < Iterations;
    }

    public static bool Verify(string password, string salt, string storedHash) {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        if (IsLegacy(storedHash)) {
            var legacy = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes((password ?? string.Empty) + salt)));
            return FixedTimeEquals(legacy, storedHash);
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        byte[] expected;
        try {
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException) {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password ?? string.Empty), Encoding.UTF8.GetBytes(salt ?? string.Empty),
            iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
