using NetPulse.Core.LoadTest;

namespace NetPulse.Tests;

[TestFixture]
public class LoadTestRequestTests
{
    private static LoadTestRequest Valid() =>
        new(LoadProtocol.Tcp, "192.168.1.50", 5201, 10, 100, 4);

    [Test]
    public void Valid_request_passes_unchanged()
    {
        var result = Valid().Validate();

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Request.Host, Is.EqualTo("192.168.1.50"));
        Assert.That(result.Request.DurationSeconds, Is.EqualTo(10));
    }

    [Test]
    public void Missing_host_is_an_error()
    {
        var result = (Valid() with { Host = "   " }).Validate();

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Contains("host"));
    }

    [Test]
    public void Host_is_trimmed()
    {
        var result = (Valid() with { Host = "  j06.local  " }).Validate();

        Assert.That(result.Request.Host, Is.EqualTo("j06.local"));
    }

    [Test]
    public void Out_of_range_port_is_an_error()
    {
        var result = (Valid() with { Port = 70000 }).Validate();

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors, Has.Some.Contains("Port"));
    }

    [Test]
    public void Duration_is_clamped_to_the_maximum_with_a_warning()
    {
        var result = (Valid() with { DurationSeconds = 10_000 }).Validate();

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Request.DurationSeconds, Is.EqualTo(LoadTestLimits.MaxDurationSeconds));
        Assert.That(result.Warnings, Has.Some.Contains("Duration"));
    }

    [Test]
    public void Parallel_streams_are_clamped()
    {
        var result = (Valid() with { ParallelStreams = 999 }).Validate();

        Assert.That(result.Request.ParallelStreams, Is.EqualTo(LoadTestLimits.MaxParallelStreams));
        Assert.That(result.Warnings, Has.Some.Contains("streams").IgnoreCase);
    }

    [Test]
    public void Bandwidth_above_the_cap_is_clamped()
    {
        var result = (Valid() with { TargetMbps = 999_999 }).Validate();

        Assert.That(result.Request.TargetMbps, Is.EqualTo(LoadTestLimits.MaxMbps));
        Assert.That(result.Warnings, Has.Some.Contains("Bandwidth"));
    }

    [Test]
    public void Negative_bandwidth_becomes_unlimited()
    {
        var result = (Valid() with { TargetMbps = -5 }).Validate();

        Assert.That(result.Request.TargetMbps, Is.Zero);
    }

    [Test]
    public void Unlimited_bandwidth_uses_the_safety_cap_as_effective_rate()
    {
        var request = Valid() with { TargetMbps = 0 };

        Assert.That(request.IsRateLimited, Is.False);
        Assert.That(request.EffectiveMbps, Is.EqualTo(LoadTestLimits.MaxMbps));
    }

    [Test]
    public void Bytes_per_second_per_stream_splits_the_rate_evenly()
    {
        // 80 Mbps over 4 streams = 20 Mbps each = 2_500_000 bytes/s each.
        var request = Valid() with { TargetMbps = 80, ParallelStreams = 4 };

        Assert.That(request.BytesPerSecondPerStream, Is.EqualTo(2_500_000).Within(0.5));
    }
}
