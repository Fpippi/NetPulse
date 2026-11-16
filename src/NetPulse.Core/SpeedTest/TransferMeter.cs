namespace NetPulse.Core.SpeedTest;

/// <summary>
/// Records the progress of a transfer (cumulative bytes over elapsed time) and computes its
/// throughput. The first part of the transfer can be skipped, because TCP slow start makes the
/// opening seconds slower than what the line can sustain.
/// </summary>
public sealed class TransferMeter
{
    private readonly List<(TimeSpan Elapsed, long Bytes)> _points = [];

    /// <summary>Bytes transferred so far.</summary>
    public long TotalBytes => _points.Count == 0 ? 0 : _points[^1].Bytes;

    /// <summary>Time of the last recorded progress point.</summary>
    public TimeSpan Elapsed => _points.Count == 0 ? TimeSpan.Zero : _points[^1].Elapsed;

    /// <summary>Records that <paramref name="totalBytes"/> (cumulative) were transferred after <paramref name="elapsed"/>.</summary>
    public void Record(TimeSpan elapsed, long totalBytes)
    {
        if (_points.Count > 0 && (elapsed < _points[^1].Elapsed || totalBytes < _points[^1].Bytes))
        {
            throw new ArgumentException("Progress must not go backwards.");
        }

        _points.Add((elapsed, totalBytes));
    }

    /// <summary>
    /// Throughput in megabits per second, measured after the first <paramref name="warmupFraction"/>
    /// of the elapsed time. Falls back to the whole transfer when too few points follow the warm-up.
    /// Null when nothing was transferred or no time elapsed.
    /// </summary>
    public double? Mbps(double warmupFraction = 0.2)
    {
        if (TotalBytes <= 0 || Elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var cutoff = Elapsed * Math.Clamp(warmupFraction, 0, 0.9);
        var start = _points.FirstOrDefault(p => p.Elapsed >= cutoff && p.Elapsed > TimeSpan.Zero);

        if (cutoff > TimeSpan.Zero && start.Elapsed < Elapsed)
        {
            return Rate(TotalBytes - start.Bytes, Elapsed - start.Elapsed);
        }

        return Rate(TotalBytes, Elapsed);
    }

    private static double Rate(long bytes, TimeSpan time) => bytes * 8.0 / time.TotalSeconds / 1_000_000.0;
}
