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
/// <item><c>1.1.1.1</c>, <c>example.com</c> — ping.</item>
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
public sealed record ProbeTarget(string Name, ProbeKind Kind, string Host, IPAddress? DnsServer = null, Uri? Url = null)
{
    public const string DnsPrefix = "dns:";

    /// <summary>Longest accepted URL, to keep target names readable and storage bounded.</summary>
    public const int MaxUrlLength = 512;

    /// <summary>A ping target for an already resolved address (used for the gateway).</summary>
    public static ProbeTarget Ping(string name, string host) => new(name, ProbeKind.Ping, host);

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
        if (!TargetName.TryNormalizeHost(value, out var host, out error))
        {
            return false;
        }

        target = new ProbeTarget(host, ProbeKind.Ping, host);
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
