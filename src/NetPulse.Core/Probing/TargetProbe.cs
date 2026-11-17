using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.Probing;

/// <summary>Timeouts per probe kind: a DNS or HTTPS round trip legitimately takes longer than a ping.</summary>
public sealed record ProbeTimeouts(TimeSpan Ping, TimeSpan Dns, TimeSpan Http)
{
    public TimeSpan For(ProbeKind kind) => kind switch
    {
        ProbeKind.Dns => Dns,
        ProbeKind.Http => Http,
        _ => Ping,
    };
}

/// <summary>
/// Sends each target to the probe that matches its <see cref="ProbeKind"/>. Host names pinned to an IP
/// version (<c>ping6:</c>, <c>ping4:</c>) are resolved in that family first.
/// </summary>
public sealed class TargetProbe(
    IPingProbe ping, ITargetProbe dns, ITargetProbe http, ProbeTimeouts timeouts, IHostResolver? resolver = null)
{
    private readonly IHostResolver _resolver = resolver ?? new SystemHostResolver();

    public ProbeTimeouts Timeouts => timeouts;

    public Task<PingResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken = default)
    {
        var timeout = timeouts.For(target.Kind);
        return target.Kind switch
        {
            ProbeKind.Dns => dns.ProbeAsync(target, timeout, cancellationToken),
            ProbeKind.Http => http.ProbeAsync(target, timeout, cancellationToken),
            _ => PingAsync(target, timeout, cancellationToken),
        };
    }

    private async Task<PingResult> PingAsync(ProbeTarget target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (target.Family == IpFamily.Any || IPAddress.TryParse(target.Host, out _))
        {
            return await ping.PingAsync(target.Host, timeout, cancellationToken);
        }

        var family = target.Family == IpFamily.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var address = await _resolver.ResolveAsync(target.Host, family, cancellationToken);
        return address is null
            ? PingResult.Failed($"{target.Host} has no {(target.Family == IpFamily.IPv6 ? "IPv6" : "IPv4")} address")
            : await ping.PingAsync(address.ToString(), timeout, cancellationToken);
    }
}
