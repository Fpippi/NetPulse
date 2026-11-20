using NetPulse.Core.Outages;

namespace NetPulse.Core.PublicIp;

/// <summary>
/// The public address was seen for the first time, or differed from the previous reading.
/// </summary>
/// <param name="Timestamp">When the new address was first seen (UTC).</param>
/// <param name="Address">The new public address.</param>
/// <param name="Previous">The address before, null for the very first reading.</param>
public sealed record PublicIpChange(DateTimeOffset Timestamp, string Address, string? Previous)
{
    /// <summary>A change this soon after an outage ended was most likely caused by it (the modem reconnected).</summary>
    public static readonly TimeSpan OutageWindow = TimeSpan.FromMinutes(15);

    public bool IsFirstReading => Previous is null;

    /// <summary>
    /// The outage that most likely caused this change: the last one that ended at most
    /// <see cref="OutageWindow"/> before it (or was still going on). Null when none did.
    /// </summary>
    public Outage? CausedBy(IEnumerable<Outage> outages) => outages
        .Where(o => o.Start <= Timestamp && (o.End ?? Timestamp) >= Timestamp - OutageWindow)
        .MaxBy(o => o.End ?? Timestamp);
}
