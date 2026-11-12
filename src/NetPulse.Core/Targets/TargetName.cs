using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.Targets;

/// <summary>
/// Validates and normalizes user-supplied ping targets (IP addresses or host names).
/// </summary>
public static class TargetName
{
    /// <summary>Maximum length of a DNS name (RFC 1035).</summary>
    public const int MaxLength = 253;

    private const int MaxLabelLength = 63;

    /// <summary>
    /// Tries to turn <paramref name="input"/> into a canonical target name.
    /// IP addresses are re-formatted, host names are trimmed and lower-cased
    /// (a trailing dot is removed).
    /// </summary>
    /// <param name="input">Raw text typed by the user.</param>
    /// <param name="target">The normalized target when the method returns true.</param>
    /// <param name="error">A human readable reason when the method returns false.</param>
    public static bool TryNormalize(string? input, out string target, out string? error)
    {
        target = string.Empty;
        var value = input?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            error = "Enter an IP address or a host name.";
            return false;
        }

        // Accept "[::1]" as typed in URLs.
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            value = value[1..^1];
        }

        if (IPAddress.TryParse(value, out var ip) && LooksLikeIp(value, ip))
        {
            target = ip.ToString();
            error = null;
            return true;
        }

        value = value.TrimEnd('.').ToLowerInvariant();

        if (value.Length == 0 || value.Length > MaxLength)
        {
            error = $"A host name must be 1-{MaxLength} characters long.";
            return false;
        }

        foreach (var label in value.Split('.'))
        {
            if (!IsValidLabel(label))
            {
                error = $"'{input!.Trim()}' is not a valid IP address or host name.";
                return false;
            }
        }

        // An all-numeric dotted name (e.g. "999.1.1.1") is a mistyped IPv4 address, not a host.
        if (value.Split('.').All(l => l.All(char.IsAsciiDigit)))
        {
            error = $"'{input!.Trim()}' is not a valid IP address.";
            return false;
        }

        target = value;
        error = null;
        return true;
    }

    /// <summary>
    /// <see cref="IPAddress.TryParse(string, out IPAddress)"/> also accepts shorthand such as "1" or
    /// "10.1"; only treat the input as an IP when it is written out in full.
    /// </summary>
    private static bool LooksLikeIp(string value, IPAddress ip) =>
        ip.AddressFamily == AddressFamily.InterNetworkV6
            ? value.Contains(':')
            : value.Count(c => c == '.') == 3 && value.Split('.').All(p => p.Length > 0 && p.All(char.IsAsciiDigit)
                && int.Parse(p, CultureInfo.InvariantCulture) <= 255);

    private static bool IsValidLabel(string label) =>
        label.Length is > 0 and <= MaxLabelLength
        && label[0] != '-'
        && label[^1] != '-'
        && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
