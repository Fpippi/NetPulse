using NetPulse.Core.Models;
using NetPulse.Core.Stats;

namespace NetPulse.Core.Outages;

/// <summary>How a degraded period changed in one <see cref="DegradationTracker.Observe"/> call.</summary>
public enum DegradationChangeKind
{
    Started,

    /// <summary>Still ongoing, with new reasons or worse values.</summary>
    Updated,
    Ended,
}

/// <summary>A degraded period that started, changed or ended, as its latest snapshot.</summary>
public sealed record DegradationChange(DegradationChangeKind Kind, Degradation Period);

/// <summary>
/// Finds degraded periods in the stream of probe samples: per target, the samples of the last
/// <c>window</c> are rated with the target's <see cref="QualityThresholds"/>, and a Poor window starts
/// a period. The period ends once the window has stayed better than Poor for another full window,
/// so a line hovering around a threshold gives one long period rather than many short ones.
/// </summary>
/// <remarks>
/// Probes lost during an outage (<c>consecutiveLosses</c> or more in a row) are left out: the
/// <see cref="OutageTracker"/> already reports those, and a degraded period means the line answered,
/// badly. A shorter run of losses counts once a reply ends it. Not thread-safe: feed it from a single monitoring loop.
/// </remarks>
public sealed class DegradationTracker
{
    private readonly TimeSpan _window;
    private readonly int _consecutiveLosses;
    private readonly int _minSamples;
    private readonly Func<string, QualityThresholds> _thresholds;
    private readonly Dictionary<string, TargetState> _targets = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="window">Length of the sliding window that is rated (at least one second).</param>
    /// <param name="consecutiveLosses">Lost probes in a row that make an outage (at least 1).</param>
    /// <param name="minSamples">Fewest counted samples a window needs to be rated (at least 2).</param>
    /// <param name="thresholds">Thresholds of a target, e.g. <see cref="QualityThresholds.ForTarget"/>.</param>
    public DegradationTracker(
        TimeSpan window, int consecutiveLosses, int minSamples, Func<string, QualityThresholds>? thresholds = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.FromSeconds(1));
        ArgumentOutOfRangeException.ThrowIfLessThan(consecutiveLosses, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minSamples, 2);
        _window = window;
        _consecutiveLosses = consecutiveLosses;
        _minSamples = minSamples;
        _thresholds = thresholds ?? QualityThresholds.ForTarget;
    }

    /// <summary>Periods that have not ended yet, ordered by target.</summary>
    public IReadOnlyList<Degradation> OpenPeriods =>
        _targets.Values
            .Where(s => s.Open is not null)
            .Select(s => s.Open!)
            .OrderBy(d => d.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Processes new samples (sorted by time here) and returns the periods that started, changed or ended.
    /// Save them with an upsert keyed on target and start.
    /// </summary>
    public IReadOnlyList<DegradationChange> Observe(IEnumerable<PingSample> samples)
    {
        var changes = new List<DegradationChange>();

        foreach (var sample in samples.OrderBy(s => s.Timestamp))
        {
            if (!_targets.TryGetValue(sample.Target, out var state))
            {
                state = new TargetState();
                _targets[sample.Target] = state;
            }

            state.Add(sample, _consecutiveLosses, _window);
            if (Evaluate(sample, state) is { } change)
            {
                changes.Add(change);
            }
        }

        // Several samples of one call can touch the same period: keep its latest snapshot only, but
        // keep Started and Ended, which drive notifications.
        return changes
            .GroupBy(c => (Target: c.Period.Target.ToLowerInvariant(), c.Period.Start))
            .SelectMany(Collapse)
            .ToList();
    }

    /// <summary>
    /// Ends the open periods of targets that are no longer monitored and forgets those targets.
    /// Returns the periods it closed.
    /// </summary>
    public IReadOnlyList<Degradation> Retain(IEnumerable<string> monitoredTargets)
    {
        var keep = new HashSet<string>(monitoredTargets, StringComparer.OrdinalIgnoreCase);
        var closed = new List<Degradation>();

        foreach (var target in _targets.Keys.Where(t => !keep.Contains(t)).ToList())
        {
            var state = _targets[target];
            if (state.Open is { } open)
            {
                closed.Add(open with { End = state.PendingEnd ?? state.LastSeen });
            }

            _targets.Remove(target);
        }

        return closed;
    }

    private DegradationChange? Evaluate(PingSample sample, TargetState state)
    {
        var counted = state.Counted();
        if (counted.Count < _minSamples)
        {
            return null;
        }

        var thresholds = _thresholds(sample.Target);
        var stats = LatencyStats.From(counted);
        var reasons = Reasons(stats, thresholds);

        if (reasons == DegradationReasons.None)
        {
            if (state.Open is null)
            {
                return null;
            }

            state.PendingEnd ??= sample.Timestamp;
            if (sample.Timestamp - state.PendingEnd.Value < _window)
            {
                return null;
            }

            var ended = state.Open with { End = state.PendingEnd };
            state.LastEnd = ended.End;
            state.Open = null;
            state.PendingEnd = null;
            return new DegradationChange(DegradationChangeKind.Ended, ended);
        }

        state.PendingEnd = null;
        if (state.Open is null)
        {
            // The window that turned Poor started earlier; do not overlap the previous period.
            var start = counted[0].Timestamp;
            if (state.LastEnd is { } lastEnd && start < lastEnd)
            {
                start = lastEnd;
            }

            state.Open = new Degradation(sample.Target, start, null, reasons, stats.LossPercent, stats.AvgMs, stats.JitterMs);
            return new DegradationChange(DegradationChangeKind.Started, state.Open);
        }

        var worse = state.Open with
        {
            Reasons = state.Open.Reasons | reasons,
            WorstLossPercent = Math.Max(state.Open.WorstLossPercent, stats.LossPercent),
            WorstAvgMs = Max(state.Open.WorstAvgMs, stats.AvgMs),
            WorstJitterMs = Max(state.Open.WorstJitterMs, stats.JitterMs),
        };

        if (worse == state.Open)
        {
            return null;
        }

        state.Open = worse;
        return new DegradationChange(DegradationChangeKind.Updated, worse);
    }

    /// <summary>Which poor thresholds a window crosses; None when it is not Poor.</summary>
    public static DegradationReasons Reasons(LatencyStats stats, QualityThresholds thresholds)
    {
        if (stats.Count == 0)
        {
            return DegradationReasons.None;
        }

        var reasons = DegradationReasons.None;
        if (stats.LossPercent >= thresholds.PoorLossPercent)
        {
            reasons |= DegradationReasons.Loss;
        }

        if (stats.AvgMs >= thresholds.PoorAvgMs)
        {
            reasons |= DegradationReasons.Latency;
        }

        if (stats.JitterMs >= thresholds.PoorJitterMs)
        {
            reasons |= DegradationReasons.Jitter;
        }

        return reasons;
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static IEnumerable<DegradationChange> Collapse(IEnumerable<DegradationChange> group)
    {
        var list = group.ToList();
        var latest = list[^1];
        var started = list.Any(c => c.Kind == DegradationChangeKind.Started);
        if (latest.Kind == DegradationChangeKind.Ended || !started)
        {
            if (started)
            {
                // Started and ended within one call: report both, the start with the final snapshot.
                yield return new DegradationChange(DegradationChangeKind.Started, latest.Period with { End = null });
            }

            yield return latest;
        }
        else
        {
            yield return new DegradationChange(DegradationChangeKind.Started, latest.Period);
        }
    }

    private sealed class TargetState
    {
        private readonly LinkedList<Entry> _entries = new();
        private readonly List<Entry> _run = [];
        private bool _inOutage;

        public Degradation? Open { get; set; }

        /// <summary>First probe after which the window was no longer Poor, while waiting to close <see cref="Open"/>.</summary>
        public DateTimeOffset? PendingEnd { get; set; }

        public DateTimeOffset? LastEnd { get; set; }

        public DateTimeOffset LastSeen { get; private set; }

        public void Add(PingSample sample, int consecutiveLosses, TimeSpan window)
        {
            LastSeen = sample.Timestamp;
            var entry = new Entry(sample);
            _entries.AddLast(entry);

            if (sample.Success)
            {
                _run.Clear();
                _inOutage = false;
            }
            else if (_inOutage)
            {
                entry.InOutage = true;
            }
            else
            {
                _run.Add(entry);
                if (_run.Count >= consecutiveLosses)
                {
                    // Part of an outage: the OutageTracker reports it.
                    foreach (var lost in _run)
                    {
                        lost.InOutage = true;
                    }

                    _run.Clear();
                    _inOutage = true;
                }
            }

            var oldest = sample.Timestamp - window;
            while (_entries.First is { } first && first.Value.Sample.Timestamp <= oldest)
            {
                _entries.RemoveFirst();
            }
        }

        /// <summary>
        /// Samples of the window that count: not part of an outage, and not part of a run of losses that
        /// could still become one (those count once a reply ends the run).
        /// </summary>
        public List<PingSample> Counted() =>
            _entries.Where(e => !e.InOutage && !_run.Contains(e)).Select(e => e.Sample).ToList();
    }

    private sealed class Entry(PingSample sample)
    {
        public PingSample Sample { get; } = sample;

        public bool InOutage { get; set; }
    }
}
