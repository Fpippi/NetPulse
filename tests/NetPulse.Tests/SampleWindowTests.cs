using NetPulse.Core.Models;
using NetPulse.Core.Stats;

namespace NetPulse.Tests;

[TestFixture]
public class SampleWindowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PingSample At(int seconds, string target = "a", double? ms = 10) =>
        new(T0.AddSeconds(seconds), target, ms.HasValue, ms);

    [Test]
    public void Empty_window_has_no_latest_and_no_targets()
    {
        var window = new SampleWindow();

        Assert.Multiple(() =>
        {
            Assert.That(window.Latest, Is.Null);
            Assert.That(window.NextSince, Is.Null);
            Assert.That(window.Targets, Is.Empty);
            Assert.That(window.For("a"), Is.Empty);
        });
    }

    [Test]
    public void Add_groups_samples_by_target_in_order()
    {
        var window = new SampleWindow();

        window.Add([At(0, "a"), At(0, "b"), At(5, "a"), At(5, "b"), At(10, "a")]);

        Assert.Multiple(() =>
        {
            Assert.That(window.Targets, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(window.For("a").Select(s => s.Timestamp), Is.EqualTo(new[] { T0, T0.AddSeconds(5), T0.AddSeconds(10) }));
            Assert.That(window.For("b"), Has.Count.EqualTo(2));
            Assert.That(window.Count, Is.EqualTo(5));
            Assert.That(window.Latest, Is.EqualTo(T0.AddSeconds(10)));
        });
    }

    [Test]
    public void Next_since_is_one_millisecond_after_the_latest_sample()
    {
        var window = new SampleWindow();

        window.Add([At(3)]);

        Assert.That(window.NextSince, Is.EqualTo(T0.AddSeconds(3).AddMilliseconds(1)));
    }

    [Test]
    public void Targets_are_case_sensitive_like_the_stored_names()
    {
        var window = new SampleWindow();

        window.Add([At(0, "Gateway"), At(0, "gateway")]);

        Assert.That(window.Targets, Has.Count.EqualTo(2));
    }

    [Test]
    public void Incremental_add_appends_after_the_cached_samples()
    {
        var window = new SampleWindow();
        window.Add([At(0), At(5)]);

        window.Add([At(10), At(15)]);

        Assert.That(window.For("a").Select(s => s.Timestamp.ToUnixTimeSeconds() - T0.ToUnixTimeSeconds()),
            Is.EqualTo(new long[] { 0, 5, 10, 15 }));
    }

    [Test]
    public void Overlapping_read_does_not_duplicate_samples()
    {
        var window = new SampleWindow();
        window.Add([At(0, "a"), At(0, "b"), At(5, "a"), At(5, "b")]);

        window.Add([At(5, "a"), At(5, "b"), At(10, "a"), At(10, "b")]);

        Assert.Multiple(() =>
        {
            Assert.That(window.For("a"), Has.Count.EqualTo(3));
            Assert.That(window.For("b"), Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Samples_sharing_a_timestamp_in_one_batch_are_all_kept()
    {
        var window = new SampleWindow();
        window.Add([At(0)]);

        window.Add([At(5, "a"), At(5, "b"), At(5, "c")]);

        Assert.That(window.Count, Is.EqualTo(4));
    }

    [Test]
    public void Trim_drops_samples_before_the_window_start()
    {
        var window = new SampleWindow();
        window.Add(Enumerable.Range(0, 10).Select(i => At(i * 5)));

        window.TrimBefore(T0.AddSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(window.For("a"), Has.Count.EqualTo(6));
            Assert.That(window.For("a")[0].Timestamp, Is.EqualTo(T0.AddSeconds(20)));
            Assert.That(window.Latest, Is.EqualTo(T0.AddSeconds(45)));
        });
    }

    [Test]
    public void Trim_removes_targets_left_without_samples()
    {
        var window = new SampleWindow();
        window.Add([At(0, "old"), At(10, "a")]);

        window.TrimBefore(T0.AddSeconds(5));

        Assert.That(window.Targets, Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void Trimming_everything_empties_the_window()
    {
        var window = new SampleWindow();
        window.Add([At(0), At(5)]);

        window.TrimBefore(T0.AddSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(window.Count, Is.Zero);
            Assert.That(window.Latest, Is.Null);
        });
    }

    [Test]
    public void Clear_forgets_everything()
    {
        var window = new SampleWindow();
        window.Add([At(0), At(5, "b")]);

        window.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(window.Count, Is.Zero);
            Assert.That(window.Latest, Is.Null);
        });

        window.Add([At(0)]);
        Assert.That(window.Count, Is.EqualTo(1), "after Clear an older sample is accepted again");
    }

    [Test]
    public void Stats_over_the_incremental_window_match_a_full_read()
    {
        var all = Enumerable.Range(0, 200).Select(i => At(i * 5, ms: i % 7 == 0 ? null : 10 + i % 13)).ToList();
        var from = T0.AddSeconds(300);
        var incremental = new SampleWindow();

        // Three refreshes, each reading only what is new, then sliding the window start.
        incremental.Add(all.Take(80));
        incremental.Add(all.Skip(70).Take(60));
        incremental.Add(all.Skip(130));
        incremental.TrimBefore(from);

        var full = all.Where(s => s.Timestamp >= from).ToList();
        Assert.That(LatencyStats.From(incremental.For("a")), Is.EqualTo(LatencyStats.From(full)));
    }
}
