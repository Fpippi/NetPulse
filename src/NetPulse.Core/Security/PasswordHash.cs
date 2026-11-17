using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NetPulse.Core.Security;

/// <summary>
/// Dashboard password check. The configured value is either a PBKDF2 hash created with
/// <see cref="Create"/> (recommended: the configuration file then holds no password) or, for a
/// quick start, the password itself.
/// </summary>
/// <remarks>
/// Hash format: <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt, base64&gt;$&lt;key, base64&gt;</c>.
/// Every comparison runs in constant time.
/// </remarks>
public static class PasswordHash
{
    public const string Prefix = "pbkdf2-sha256$";

    /// <summary>OWASP's 2023 recommendation for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    /// <summary>Upper bound on the iterations read from a hash, so a mistyped value cannot stall logins.</summary>
    private const int MaxIterations = 10_000_000;

    /// <summary>True when <paramref name="configured"/> is a hash rather than a plain password.</summary>
    public static bool IsHash(string? configured) =>
        configured is not null && configured.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Hashes <paramref name="password"/> with a random salt.</summary>
    public static string Create(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxIterations);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}{iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}");
    }

    /// <summary>
    /// Checks <paramref name="password"/> against the configured hash or plain password.
    /// An empty password or a malformed hash never matches.
    /// </summary>
    public static bool Verify(string? password, string? configured)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(configured))
        {
            return false;
        }

        if (!IsHash(configured))
        {
            return FixedTimeEquals(password, configured);
        }

        if (!TryParse(configured, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>True when <paramref name="configured"/> is a hash that <see cref="Verify"/> can read.</summary>
    public static bool IsValidHash(string configured) => TryParse(configured, out _, out _, out _);

    /// <summary>
    /// Constant-time comparison of two secrets of any length (API tokens, plain passwords):
    /// both are hashed first, so neither the content nor the length leaks through timing.
    /// </summary>
    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    private static bool TryParse(string value, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = key = [];

        var parts = value[Prefix.Length..].Split('$');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations is < 1 or > MaxIterations)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[1]);
            key = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= 8 && key.Length >= 16;
    }
}
