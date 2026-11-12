using NetPulse.Core.Models;

namespace NetPulse.Core.Outages;

/// <summary>
/// Turns the stream of ping samples into outages. An outage starts (retroactively, at the first lost
/// ping) once <c>consecutiveLosses</c> pings in a row are lost, and ends at the next successful ping.
/// Shorter runs of loss are ordinary packet loss and are not reported.
/// </summary>
/// <remarks>Not thread-safe: feed it from a single monitoring loop.</remarks>
public sealed class OutageTracker
{
    private readonly int _consecutiveLosses;
    private readonly Dictionary<string, TargetState> _targets = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="consecutiveLosses">Lost pings in a row that make an outage (at least 1).</param>
    public OutageTracker(int consecutiveLosses)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(consecutiveLosses, 1);
        _consecutiveLosses = consecutiveLosses;
    }

    public int ConsecutiveLosses => _consecutiveLosses;

    /// <summary>Outages that have not ended yet, ordered by target.</summary>
    public IReadOnlyList<Outage> OpenOutages =>
        _targets.Values
            .Where(s => s.Open is not null)
            .Select(s => s.Open!)
            .OrderBy(o => o.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Processes new samples (in any order; they are sorted by time per target) and returns every
    /// outage that started, grew or ended, as its latest snapshot. Save them with an upsert keyed
    /// on target and start.
    /// </summary>
    public IReadOnlyList<Outage> Observe(IEnumerable<PingSample> samples)
    {
        var changed = new Changes();

        foreach (var sample in samples.OrderBy(s => s.Timestamp))
        {
            if (!_targets.TryGetValue(sample.Target, out var state))
            {
                state = new TargetState();
                _targets[sample.Target] = state;
            }

            if (sample.Success)
            {
                if (state.Open is not null)
                {
                    changed.Set(state.Open with { End = sample.Timestamp });
                    state.Open = null;
                }

                state.RunLength = 0;
                continue;
            }

            if (state.RunLength == 0)
            {
                state.RunStart = sample.Timestamp;
            }

            state.RunLength++;

            if (state.Open is not null)
            {
                state.Open = state.Open with { LastLost = sample.Timestamp, LostSamples = state.Open.LostSamples + 1 };
                changed.Set(state.Open);
            }
            else if (state.RunLength >= _consecutiveLosses)
            {
                state.Open = new Outage(sample.Target, state.RunStart, sample.Timestamp, null, state.RunLength);
                changed.Set(state.Open);
            }
        }

        return changed.ToList();
    }

    /// <summary>
    /// Ends the open outages of targets that are no longer monitored (e.g. removed from the dashboard)
    /// at their last lost ping, and forgets those targets. Returns the outages it closed.
    /// </summary>
    public IReadOnlyList<Outage> Retain(IEnumerable<string> monitoredTargets)
    {
        var keep = new HashSet<string>(monitoredTargets, StringComparer.OrdinalIgnoreCase);
        var closed = new List<Outage>();

        foreach (var target in _targets.Keys.Where(t => !keep.Contains(t)).ToList())
        {
            if (_targets[target].Open is { } open)
            {
                closed.Add(open with { End = open.LastLost });
            }

            _targets.Remove(target);
        }

        return closed;
    }

    private sealed class TargetState
    {
        public DateTimeOffset RunStart { get; set; }

        public int RunLength { get; set; }

        public Outage? Open { get; set; }
    }

    /// <summary>Keeps only the latest snapshot of each outage, in first-change order.</summary>
    private sealed class Changes
    {
        private readonly List<(string Target, DateTimeOffset Start)> _order = [];
        private readonly Dictionary<(string, DateTimeOffset), Outage> _latest = new(new KeyComparer());

        public void Set(Outage outage)
        {
            var key = (outage.Target, outage.Start);
            if (!_latest.ContainsKey(key))
            {
                _order.Add(key);
            }

            _latest[key] = outage;
        }

        public List<Outage> ToList() => _order.Select(k => _latest[k]).ToList();

        private sealed class KeyComparer : IEqualityComparer<(string Target, DateTimeOffset Start)>
        {
            public bool Equals((string Target, DateTimeOffset Start) x, (string Target, DateTimeOffset Start) y) =>
                string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase) && x.Start == y.Start;

            public int GetHashCode((string Target, DateTimeOffset Start) key) =>
                HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Target), key.Start);
        }
    }
}
