using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.ClientCheck;

/// <summary>Why no address for other devices could be worked out.</summary>
public enum LanUrlProblem
{
    None,

    /// <summary>The server only listens on the loopback interface, so nothing else on the network can connect.</summary>
    LoopbackOnly,

    /// <summary>This machine has no IPv4 address on a local network.</summary>
    NoLanAddress,
}

/// <summary>The address another device on the network (a phone) can open, or why there is none.</summary>
public sealed record LanUrlResult(string? Url, LanUrlProblem Problem)
{
    public static LanUrlResult Of(string url) => new(url, LanUrlProblem.None);

    public static LanUrlResult Failed(LanUrlProblem problem) => new(null, problem);
}

/// <summary>
/// Builds the dashboard's address as seen from the local network, e.g. <c>http://192.168.1.10:5080/check</c>,
/// from the addresses the server listens on and this machine's LAN address. <c>localhost</c> would only
/// open the phone itself, so it is never used.
/// </summary>
public static class LanUrl
{
    /// <param name="listenUrls">Server addresses, e.g. <c>http://0.0.0.0:5080</c>, <c>http://[::]:5080</c>, <c>http://+:80</c>.</param>
    /// <param name="lanAddress">This machine's IPv4 address on the LAN, if any.</param>
    /// <param name="path">Path to open, e.g. <c>/check</c>.</param>
    /// <param name="configuredUrl">
    /// An address set by the user (reverse proxy, host name): used as is, with the path appended.
    /// </param>
    public static LanUrlResult Resolve(
        IEnumerable<string> listenUrls, IPAddress? lanAddress, string path, string? configuredUrl = null)
    {
        path = "/" + path.TrimStart('/');
        if (!string.IsNullOrWhiteSpace(configuredUrl)
            && Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var configured)
            && configured.Scheme is "http" or "https")
        {
            return LanUrlResult.Of(configured.GetLeftPart(UriPartial.Authority) + configured.AbsolutePath.TrimEnd('/') + path);
        }

        var bindings = listenUrls.Select(Parse).OfType<Binding>().ToList();
        if (lanAddress is null || lanAddress.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(lanAddress))
        {
            return LanUrlResult.Failed(bindings.Count > 0 && bindings.All(b => b.Loopback)
                ? LanUrlProblem.LoopbackOnly
                : LanUrlProblem.NoLanAddress);
        }

        // Plain HTTP first: a phone rejects the self-signed certificates a home server usually has.
        var usable = bindings
            .Where(b => b.Any || lanAddress.Equals(b.Address))
            .OrderBy(b => b.Scheme == "http" ? 0 : 1)
            .FirstOrDefault();

        if (usable is null)
        {
            return LanUrlResult.Failed(LanUrlProblem.LoopbackOnly);
        }

        var port = usable.Port == (usable.Scheme == "https" ? 443 : 80) ? string.Empty : $":{usable.Port}";
        return LanUrlResult.Of($"{usable.Scheme}://{lanAddress}{port}{path}");
    }

    private sealed record Binding(string Scheme, int Port, bool Any, bool Loopback, IPAddress? Address);

    private static Binding? Parse(string url)
    {
        // Kestrel's wildcards ("+", "*") are not valid URI hosts, so they are swapped before parsing.
        var normalized = url.Trim().Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0");
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var host = uri.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return new Binding(uri.Scheme, uri.Port, false, true, null);
        }

        if (!IPAddress.TryParse(host, out var ip))
        {
            // A host name binding listens on every interface.
            return new Binding(uri.Scheme, uri.Port, true, false, null);
        }

        var any = ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any);
        return new Binding(uri.Scheme, uri.Port, any, IPAddress.IsLoopback(ip), any ? null : ip);
    }
}
