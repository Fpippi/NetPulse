using NetPulse.Core.Models;

namespace NetPulse.Core.Stats;

/// <summary>
/// The samples of a sliding time window, grouped by target and kept in timestamp order, so a page that
/// refreshes every few seconds only has to read the samples stored since its last refresh.
/// </summary>
/// <remarks>
/// The monitor stores each cycle's samples in one transaction with one shared timestamp, so reading
/// everything after <see cref="Latest"/> never returns half a cycle or a sample twice.
/// </remarks>
public sealed class SampleWindow
{
    private readonly Dictionary<string, List<PingSample>> _byTarget = new(StringComparer.Ordinal);

    /// <summary>Timestamp of the newest sample added since the last <see cref="Clear"/>; null when empty.</summary>
    public DateTimeOffset? Latest { get; private set; }

    /// <summary>Lower bound for the next incremental read: just after <see cref="Latest"/> (storage has ms resolution).</summary>
    public DateTimeOffset? NextSince => Latest?.AddMilliseconds(1);

    /// <summary>Targets with at least one sample, in no particular order.</summary>
    public IEnumerable<string> Targets => _byTarget.Keys;

    /// <summary>Total number of samples held, over every target.</summary>
    public int Count => _byTarget.Values.Sum(list => list.Count);

    public void Clear()
    {
        _byTarget.Clear();
        Latest = null;
    }

    /// <summary>
    /// Appends samples ordered by timestamp. Samples not newer than <see cref="Latest"/> (as it was before
    /// this call) are ignored, so an overlapping read cannot duplicate them.
    /// </summary>
    public void Add(IEnumerable<PingSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var previous = Latest;
        foreach (var sample in samples)
        {
            if (previous is { } p && sample.Timestamp <= p)
            {
                continue;
            }

            if (!_byTarget.TryGetValue(sample.Target, out var list))
            {
                list = [];
                _byTarget[sample.Target] = list;
            }

            list.Add(sample);
            if (Latest is null || sample.Timestamp > Latest)
            {
                Latest = sample.Timestamp;
            }
        }
    }

    /// <summary>Drops the samples older than <paramref name="from"/>, and targets left without samples.</summary>
    public void TrimBefore(DateTimeOffset from)
    {
        foreach (var (target, list) in _byTarget.ToList())
        {
            var keep = FirstAtOrAfter(list, from);
            if (keep == list.Count)
            {
                _byTarget.Remove(target);
            }
            else if (keep > 0)
            {
                list.RemoveRange(0, keep);
            }
        }

        if (_byTarget.Count == 0)
        {
            Latest = null;
        }
    }

    /// <summary>The samples of one target in timestamp order; empty if it has none.</summary>
    /// <remarks>The list is live: it changes on the next <see cref="Add"/> or <see cref="TrimBefore"/>.</remarks>
    public IReadOnlyList<PingSample> For(string target) =>
        _byTarget.TryGetValue(target, out var list) ? list : [];

    /// <summary>Binary search: index of the first sample at or after <paramref name="from"/>.</summary>
    private static int FirstAtOrAfter(List<PingSample> list, DateTimeOffset from)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (list[mid].Timestamp < from)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
