using System.Net;

namespace NetPulse.Core.PublicIp;

/// <summary>
/// Reads the public address out of a "what is my IP" answer: Cloudflare's <c>/cdn-cgi/trace</c>
/// (<c>key=value</c> lines, <c>ip=…</c>) or a service that returns the bare address (ipify, icanhazip).
/// </summary>
public static class PublicIpResponse
{
    /// <summary>
    /// True when <paramref name="body"/> holds a public address. Private or carrier-grade NAT addresses
    /// are refused: a "what is my IP" service never sees them, so the answer did not come from it
    /// (e.g. a captive portal).
    /// </summary>
    public static bool TryParse(string? body, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var text = body.Trim();
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim() is var trimmed && trimmed.StartsWith("ip=", StringComparison.Ordinal))
            {
                text = trimmed[3..];
                break;
            }
        }

        if (!IPAddress.TryParse(text, out var parsed) || !AddressScope.IsPublic(parsed))
        {
            return false;
        }

        // TryParse also accepts "1" or "1.2" (= 0.0.0.1 / 1.0.0.2); only full IPv4 or IPv6 notation counts.
        if (!text.Contains(':') && text.Count(c => c == '.') != 3)
        {
            return false;
        }

        address = parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed;
        return true;
    }
}
