using NetPulse.Core.Outages;

namespace NetPulse.Tests.Outages;

[TestFixture]
public class OutageSummaryTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddHours(10);

    private static Outage Outage(double startMinutes, double? endMinutes) => new(
        "1.1.1.1",
        From.AddMinutes(startMinutes),
        From.AddMinutes(endMinutes ?? startMinutes),
        endMinutes is null ? null : From.AddMinutes(endMinutes.Value),
        3);

    [Test]
    public void No_outages_means_full_availability()
    {
        var summary = OutageSummary.From([], From, To);

        Assert.That(summary, Is.EqualTo(new OutageSummary(0, TimeSpan.Zero, null, 100)));
    }

    [Test]
    public void Sums_downtime_and_finds_the_longest_outage()
    {
        var summary = OutageSummary.From([Outage(10, 16), Outage(60, 90)], From, To);

        Assert.That(summary.Count, Is.EqualTo(2));
        Assert.That(summary.Downtime, Is.EqualTo(TimeSpan.FromMinutes(36)));
        Assert.That(summary.Longest, Is.EqualTo(TimeSpan.FromMinutes(30)));
        Assert.That(summary.AvailabilityPercent, Is.EqualTo(94).Within(1e-9));
    }

    [Test]
    public void Clips_outages_to_the_window_and_runs_ongoing_ones_until_its_end()
    {
        var summary = OutageSummary.From([Outage(-30, 30), Outage(540, null)], From, To);

        Assert.That(summary.Count, Is.EqualTo(2));
        Assert.That(summary.Downtime, Is.EqualTo(TimeSpan.FromMinutes(30 + 60)));
        Assert.That(summary.AvailabilityPercent, Is.EqualTo(85).Within(1e-9));
    }

    [Test]
    public void Ignores_outages_outside_the_window()
    {
        var summary = OutageSummary.From([Outage(-60, -10), Outage(-10, 0), Outage(700, 710)], From, To);

        Assert.That(summary.Count, Is.Zero);
    }

    [Test]
    public void Counts_a_zero_length_outage_inside_the_window()
    {
        var summary = OutageSummary.From([Outage(5, 5)], From, To);

        Assert.That(summary.Count, Is.EqualTo(1));
        Assert.That(summary.AvailabilityPercent, Is.EqualTo(100));
    }
}
