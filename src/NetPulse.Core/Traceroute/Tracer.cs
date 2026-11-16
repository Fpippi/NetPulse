namespace NetPulse.Core.Traceroute;

/// <summary>Limits of a traceroute.</summary>
/// <param name="MaxHops">Farthest distance probed.</param>
/// <param name="Timeout">How long to wait for each hop.</param>
/// <param name="StopAfterSilentHops">
/// Give up after this many silent hops in a row: during an outage the path is usually cut, and probing
/// every remaining TTL would only add timeouts.
/// </param>
public sealed record TraceOptions(int MaxHops = 20, TimeSpan? Timeout = null, int StopAfterSilentHops = 4)
{
    public TimeSpan HopTimeout => Timeout ?? TimeSpan.FromSeconds(1);
}

/// <summary>
/// Classic traceroute: probes with TTL 1, 2, 3… and records who answers, until the destination
/// replies, too many hops in a row stay silent, or <see cref="TraceOptions.MaxHops"/> is reached.
/// </summary>
public sealed class Tracer(ITraceProbe probe)
{
    /// <summary>
    /// Plain IP addresses and host names only. The gateway is one hop away (nothing to trace), and
    /// other target kinds (e.g. "dns:…" or URLs) are not host names.
    /// </summary>
    public static bool IsTraceable(string target, string gatewayTarget) =>
        !string.Equals(target, gatewayTarget, StringComparison.OrdinalIgnoreCase)
        && Uri.CheckHostName(target) != UriHostNameType.Unknown;

    public async Task<TraceResult> RunAsync(
        string host, TraceOptions options, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        var hops = new List<TraceHop>();
        var silent = 0;

        for (var ttl = 1; ttl <= Math.Clamp(options.MaxHops, 1, 64); ttl++)
        {
            var reply = await probe.SendAsync(host, ttl, options.HopTimeout, cancellationToken);
            hops.Add(new TraceHop(ttl, reply.Status, reply.Address?.ToString(), reply.RttMs));

            if (reply.Status == HopStatus.Reached)
            {
                break;
            }

            silent = reply.Address is null ? silent + 1 : 0;
            if (silent >= Math.Max(1, options.StopAfterSilentHops))
            {
                break;
            }
        }

        // Trailing silent hops only say "nothing beyond": keep one as a marker, drop the rest.
        while (hops.Count >= 2 && hops[^1].Address is null && hops[^2].Address is null)
        {
            hops.RemoveAt(hops.Count - 1);
        }

        return new TraceResult(host, timestamp, hops);
    }
}
