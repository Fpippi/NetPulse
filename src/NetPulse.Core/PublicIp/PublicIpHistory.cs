using NetPulse.Core.Outages;

namespace NetPulse.Core.PublicIp;

/// <summary>Public address changes of a period, oldest first, with what can be said about them.</summary>
public sealed class PublicIpHistory(IReadOnlyList<PublicIpChange> changes)
{
    public static readonly PublicIpHistory Empty = new([]);

    public IReadOnlyList<PublicIpChange> Changes { get; } = changes.OrderBy(c => c.Timestamp).ToList();

    /// <summary>Real changes, without the first reading of a fresh install.</summary>
    public IEnumerable<PublicIpChange> RealChanges => Changes.Where(c => !c.IsFirstReading);

    /// <summary>How many real changes followed an outage (see <see cref="PublicIpChange.CausedBy"/>).</summary>
    public int AfterOutages(IReadOnlyList<Outage> outages) => RealChanges.Count(c => c.CausedBy(outages) is not null);
}
