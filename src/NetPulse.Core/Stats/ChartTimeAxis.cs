namespace NetPulse.Core.Stats;

/// <summary>
/// Maps timestamps to horizontal chart coordinates for a fixed time window.
/// Using the window (not the data extent) keeps the axis meaningful: 15 minutes of data
/// fill a 15-minute chart but only a sliver of a 24-hour one.
/// </summary>
public static class ChartTimeAxis
{
    /// <summary>
    /// Returns the x coordinate of <paramref name="time"/> in a chart of <paramref name="width"/> units
    /// spanning <paramref name="from"/> (x = 0) to <paramref name="to"/> (x = width), clamped to the chart.
    /// </summary>
    public static double ToX(DateTimeOffset time, DateTimeOffset from, DateTimeOffset to, double width)
    {
        if (to <= from)
        {
            throw new ArgumentException("The window end must be after its start.", nameof(to));
        }

        var fraction = (time - from) / (to - from);
        return Math.Clamp(fraction, 0, 1) * width;
    }
}
