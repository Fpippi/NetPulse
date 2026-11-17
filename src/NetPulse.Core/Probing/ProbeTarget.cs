using System.Net;
using NetPulse.Core.Targets;

namespace NetPulse.Core.Probing;

/// <summary>
/// A monitored target parsed into what to probe and how. The target name stays the key used in
/// samples, outages and charts, so DNS and HTTP targets get statistics, outage detection and
/// reports for free.
/// </summary>
/// <remarks>
/// Accepted forms:
/// <list type="bullet">
/// <item><c>1.1.1.1</c>, <c>2606:4700:4700::1111</c>, <c>example.com</c> — ping.</item>
/// <item><c>ping6:example.com</c>, <c>ping4:example.com</c> — ping a host name over IPv6 or IPv4 only.</item>
/// <item><c>dns:example.com</c> — resolve the name with the system's DNS server;
///   <c>dns:example.com@1.1.1.1</c> — with a specific server.</item>
/// <item><c>https://example.com/</c>, <c>http://192.168.1.1/</c> — HTTP request.</item>
/// </list>
/// </remarks>
/// <param name="Name">Canonical target name, as stored in samples.</param>
/// <param name="Kind">How the target is probed.</param>
/// <param name="Host">Host to ping, name to resolve, or host of the URL.</param>
/// <param name="DnsServer">DNS server to ask (DNS targets only); null means the system's server.</param>
/// <param name="Url">URL to request (HTTP targets only).</param>
/// <param name="Family">IP version to ping a host name over (<c>ping4:</c> / <c>ping6:</c>); Any otherwise.</param>
public sealed record ProbeTarget(
    string Name, ProbeKind Kind, string Host, IPAddress? DnsServer = null, Uri? Url = null, IpFamily Family = IpFamily.Any)
{
    public const string DnsPrefix = "dns:";
    public const string Ping4Prefix = "ping4:";
    public const string Ping6Prefix = "ping6:";

    /// <summary>Special targets resolved at runtime to the default IPv4 / IPv6 router.</summary>
    public const string Gateway4 = "gateway";
    public const string Gateway6 = "gateway6";

    /// <summary>Longest accepted URL, to keep target names readable and storage bounded.</summary>
    public const int MaxUrlLength = 512;

    /// <summary>A ping target for an already resolved address (used for the gateway).</summary>
    public static ProbeTarget Ping(string name, string host) => new(name, ProbeKind.Ping, host);

    /// <summary>
    /// Cheap classification of a (valid) target name by IP version: literal addresses, <c>ping4:</c> /
    /// <c>ping6:</c>, the two gateways, URLs and DNS servers written as addresses. Host names are Any.
    /// </summary>
    public static IpFamily FamilyOf(string name)
    {
        if (string.Equals(name, Gateway6, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(Ping6Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return IpFamily.IPv6;
        }

        if (string.Equals(name, Gateway4, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(Ping4Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return IpFamily.IPv4;
        }

        var host = KindOf(name) switch
        {
            ProbeKind.Dns => name[(name.LastIndexOf('@') + 1)..],
            ProbeKind.Http => Uri.TryCreate(name, UriKind.Absolute, out var uri) ? uri.Host.Trim('[', ']') : string.Empty,
            _ => name,
        };

        return IPAddress.TryParse(host, out var ip) && (host.Contains(':') || host.Count(c => c == '.') == 3)
            ? ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? IpFamily.IPv6 : IpFamily.IPv4
            : IpFamily.Any;
    }

    /// <summary>Cheap classification of a (valid) target name, e.g. to pick quality thresholds.</summary>
    public static ProbeKind KindOf(string name) =>
        name.StartsWith(DnsPrefix, StringComparison.OrdinalIgnoreCase) ? ProbeKind.Dns
        : IsHttp(name) ? ProbeKind.Http
        : ProbeKind.Ping;

    /// <summary>
    /// Parses and normalizes a target. Host names and URL hosts are lower-cased,
    /// IP addresses re-formatted, and URLs written in their canonical form.
    /// </summary>
    public static bool TryParse(string? input, out ProbeTarget target, out string? error)
    {
        target = null!;
        var value = input?.Trim() ?? string.Empty;

        return KindOf(value) switch
        {
            ProbeKind.Dns => TryParseDns(input!, value[DnsPrefix.Length..], out target, out error),
            ProbeKind.Http => TryParseHttp(input!, value, out target, out error),
            _ => TryParsePing(value, out target, out error),
        };
    }

    private static bool TryParsePing(string value, out ProbeTarget target, out string? error)
    {
        target = null!;
        var family = value.StartsWith(Ping6Prefix, StringComparison.OrdinalIgnoreCase) ? IpFamily.IPv6
            : value.StartsWith(Ping4Prefix, StringComparison.OrdinalIgnoreCase) ? IpFamily.IPv4
            : IpFamily.Any;

        if (family == IpFamily.Any)
        {
            if (!TargetName.TryNormalizeHost(value, out var plain, out error))
            {
                return false;
            }

            target = new ProbeTarget(plain, ProbeKind.Ping, plain, Family: FamilyOf(plain));
            return true;
        }

        var prefix = family == IpFamily.IPv6 ? Ping6Prefix : Ping4Prefix;
        if (!TargetName.TryNormalizeHost(value[prefix.Length..], out var host, out _)
            || IPAddress.TryParse(host, out _)
            || host is Gateway4 or Gateway6)
        {
            error = $"'{value}': write a host name after '{prefix}', e.g. {prefix}example.com (addresses need no prefix).";
            return false;
        }

        target = new ProbeTarget(prefix + host, ProbeKind.Ping, host, Family: family);
        error = null;
        return true;
    }

    private static bool TryParseDns(string input, string spec, out ProbeTarget target, out string? error)
    {
        target = null!;
        var at = spec.LastIndexOf('@');
        var name = at < 0 ? spec : spec[..at];
        var serverText = at < 0 ? null : spec[(at + 1)..].Trim('[', ']');

        if (!TargetName.TryNormalizeHost(name, out var host, out _) || IPAddress.TryParse(host, out _))
        {
            error = $"'{input.Trim()}': write the name to resolve after 'dns:', e.g. dns:example.com.";
            return false;
        }

        IPAddress? server = null;
        if (serverText is not null
            && (!TargetName.TryNormalizeHost(serverText, out var serverHost, out _)
                || !IPAddress.TryParse(serverHost, out server)))
        {
            error = $"'{input.Trim()}': the DNS server after '@' must be an IP address, e.g. dns:example.com@1.1.1.1.";
            return false;
        }

        var canonical = DnsPrefix + host + (server is null ? string.Empty : "@" + server);
        target = new ProbeTarget(canonical, ProbeKind.Dns, host, DnsServer: server);
        error = null;
        return true;
    }

    private static bool TryParseHttp(string input, string value, out ProbeTarget target, out string? error)
    {
        target = null!;
        if (value.Length > MaxUrlLength
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || value.Any(char.IsWhiteSpace))
        {
            error = $"'{input.Trim()}' is not a valid http(s) URL.";
            return false;
        }

        if (uri.UserInfo.Length > 0)
        {
            error = "URLs with a user name or password are not supported (they would be stored in clear text).";
            return false;
        }

        var bareHost = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host;
        if (!TargetName.TryNormalizeHost(bareHost, out _, out _))
        {
            error = $"'{input.Trim()}' does not contain a valid host name.";
            return false;
        }

        // Drop the fragment (never sent to the server) and keep the canonical form.
        var canonical = new UriBuilder(uri) { Fragment = string.Empty }.Uri;
        target = new ProbeTarget(canonical.AbsoluteUri, ProbeKind.Http, canonical.Host, Url: canonical);
        error = null;
        return true;
    }

    private static bool IsHttp(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
