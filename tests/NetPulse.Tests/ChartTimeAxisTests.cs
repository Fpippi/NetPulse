using NetPulse.Core.Stats;

namespace NetPulse.Tests;

[TestFixture]
public class ChartTimeAxisTests
{
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddHours(24);

    [Test]
    public void Window_edges_map_to_chart_edges()
    {
        Assert.That(ChartTimeAxis.ToX(From, From, To, 600), Is.EqualTo(0));
        Assert.That(ChartTimeAxis.ToX(To, From, To, 600), Is.EqualTo(600));
    }

    [Test]
    public void Position_is_proportional_to_time()
    {
        Assert.That(ChartTimeAxis.ToX(From.AddHours(6), From, To, 600), Is.EqualTo(150).Within(1e-9));
    }

    [Test]
    public void Recent_data_occupies_only_the_end_of_a_long_window()
    {
        // One minute of data in a 24h window sits in the last ~0.07% of the chart.
        var x = ChartTimeAxis.ToX(To.AddMinutes(-1), From, To, 600);

        Assert.That(x, Is.GreaterThan(599));
    }

    [Test]
    public void Times_outside_the_window_are_clamped()
    {
        Assert.That(ChartTimeAxis.ToX(From.AddHours(-1), From, To, 600), Is.EqualTo(0));
        Assert.That(ChartTimeAxis.ToX(To.AddHours(1), From, To, 600), Is.EqualTo(600));
    }

    [Test]
    public void Empty_window_throws()
    {
        Assert.Throws<ArgumentException>(() => ChartTimeAxis.ToX(From, From, From, 600));
    }
}
