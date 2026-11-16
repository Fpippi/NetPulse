using NetPulse.Core.Probing;

namespace NetPulse.Tests.Probing;

[TestFixture]
public class TargetProbeTests
{
    private static readonly ProbeTimeouts Timeouts = new(
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

    [TestCase("1.1.1.1", "ping", 1)]
    [TestCase("dns:example.com", "dns", 2)]
    [TestCase("https://example.com", "http", 5)]
    public async Task Dispatches_by_kind_with_its_timeout(string input, string expectedProbe, int timeoutSeconds)
    {
        var calls = new List<(string Probe, TimeSpan Timeout)>();
        var probe = new TargetProbe(
            new FakePing(calls), new FakeTarget("dns", calls), new FakeTarget("http", calls), Timeouts);
        ProbeTarget.TryParse(input, out var target, out _);

        var result = await probe.ProbeAsync(target);

        Assert.That(result.Success, Is.True);
        Assert.That(calls, Is.EqualTo(new[] { (expectedProbe, TimeSpan.FromSeconds(timeoutSeconds)) }));
    }

    [Test]
    public async Task Pings_the_resolved_host_not_the_name()
    {
        var calls = new List<(string Probe, TimeSpan Timeout)>();
        var ping = new FakePing(calls);
        var probe = new TargetProbe(ping, new FakeTarget("dns", calls), new FakeTarget("http", calls), Timeouts);

        await probe.ProbeAsync(ProbeTarget.Ping("gateway", "192.168.1.1"));

        Assert.That(ping.LastHost, Is.EqualTo("192.168.1.1"));
    }

    private sealed class FakePing(List<(string, TimeSpan)> calls) : IPingProbe
    {
        public string? LastHost { get; private set; }

        public Task<PingResult> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastHost = host;
            calls.Add(("ping", timeout));
            return Task.FromResult(PingResult.Ok(1));
        }
    }

    private sealed class FakeTarget(string name, List<(string, TimeSpan)> calls) : ITargetProbe
    {
        public Task<PingResult> ProbeAsync(ProbeTarget target, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            calls.Add((name, timeout));
            return Task.FromResult(PingResult.Ok(1));
        }
    }
}
