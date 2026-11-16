using NetPulse.Core.Outages;

namespace NetPulse.Core.Traceroute;

/// <summary>Finds the traceroute taken for an outage (same target, same start).</summary>
public sealed class TraceLookup
{
    private readonly Dictionary<(string Target, long Start), TraceResult> _traces;

    public TraceLookup(IEnumerable<OutageTrace> traces)
    {
        _traces = new Dictionary<(string, long), TraceResult>(KeyComparer.Instance);
        foreach (var trace in traces)
        {
            _traces[(trace.Target, trace.OutageStart.ToUnixTimeMilliseconds())] = trace.Trace;
        }
    }

    public static TraceLookup Empty { get; } = new([]);

    public TraceResult? Find(Outage outage) =>
        _traces.GetValueOrDefault((outage.Target, outage.Start.ToUnixTimeMilliseconds()));

    private sealed class KeyComparer : IEqualityComparer<(string Target, long Start)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals((string Target, long Start) x, (string Target, long Start) y) =>
            x.Start == y.Start && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Target, long Start) key) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Target), key.Start);
    }
}
