using System.Net;

namespace NetPulse.Core.Traceroute;

public enum HopStatus
{
    /// <summary>A router on the way answered "time exceeded": the packet got this far.</summary>
    TtlExpired,

    /// <summary>The destination itself answered.</summary>
    Reached,

    /// <summary>Nothing answered at this distance (filtered, or the path is broken here).</summary>
    Timeout,

    /// <summary>The probe could not be sent (e.g. no route).</summary>
    Error,
}

/// <summary>The answer to one probe sent with a given TTL.</summary>
public sealed record HopReply(HopStatus Status, IPAddress? Address, double? RttMs)
{
    public static HopReply Silent { get; } = new(HopStatus.Timeout, null, null);
}

/// <summary>Sends one probe with a limited TTL. Abstracted so the traceroute logic runs without a network.</summary>
public interface ITraceProbe
{
    Task<HopReply> SendAsync(string host, int ttl, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>One line of a traceroute.</summary>
/// <param name="Number">Distance from this machine, 1 = the first router.</param>
/// <param name="Address">Who answered, null when nothing did.</param>
/// <param name="RttMs">Round-trip time of the answer.</param>
public sealed record TraceHop(int Number, HopStatus Status, string? Address, double? RttMs);

/// <summary>The path towards a target, as far as it could be followed.</summary>
public sealed record TraceResult(string Target, DateTimeOffset Timestamp, IReadOnlyList<TraceHop> Hops)
{
    public bool Reached => Hops.Any(h => h.Status == HopStatus.Reached);

    /// <summary>The farthest hop that answered, null if none did.</summary>
    public TraceHop? LastResponding => Hops.LastOrDefault(h => h.Address is not null);

    /// <summary>One sentence for tables and reports.</summary>
    public string Summary => (Reached, LastResponding) switch
    {
        (true, var last) => $"Reached {Target} in {last!.Number} hops",
        (false, null) => "No hop answered, not even the router",
        (false, { Number: 1 } last) => $"Stopped after your router ({last.Address}); nothing answered beyond it",
        (false, var last) => $"Stopped after hop {last!.Number} ({last.Address}); nothing answered beyond it",
    };
}

/// <summary>A traceroute taken while <see cref="Outages.Outage"/> of <see cref="Target"/> started at <see cref="OutageStart"/>.</summary>
public sealed record OutageTrace(string Target, DateTimeOffset OutageStart, TraceResult Trace);
