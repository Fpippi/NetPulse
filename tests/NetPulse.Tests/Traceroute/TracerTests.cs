using System.Net;
using NetPulse.Core.Outages;
using NetPulse.Core.Traceroute;

namespace NetPulse.Tests.Traceroute;

[TestFixture]
public class TracerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static HopReply Hop(string ip, double ms = 5) => new(HopStatus.TtlExpired, IPAddress.Parse(ip), ms);

    private static HopReply Dest(string ip) => new(HopStatus.Reached, IPAddress.Parse(ip), 12);

    [Test]
    public async Task Stops_when_the_destination_answers()
    {
        var probe = new ScriptedProbe(Hop("192.168.1.1"), Hop("10.0.0.1"), Dest("1.1.1.1"), Hop("9.9.9.9"));

        var trace = await new Tracer(probe).RunAsync("1.1.1.1", new TraceOptions(), T0);

        Assert.That(trace.Hops.Select(h => h.Address), Is.EqualTo(new[] { "192.168.1.1", "10.0.0.1", "1.1.1.1" }));
        Assert.That(trace.Reached, Is.True);
        Assert.That(trace.Summary, Is.EqualTo("Reached 1.1.1.1 in 3 hops"));
        Assert.That(probe.Ttls, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Gives_up_after_silent_hops_and_keeps_one_marker()
    {
        var probe = new ScriptedProbe(Hop("192.168.1.1"), Hop("100.64.0.1"));

        var trace = await new Tracer(probe).RunAsync("8.8.8.8", new TraceOptions(StopAfterSilentHops: 3), T0);

        Assert.That(probe.Ttls, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }), "two answers, then three silent hops");
        Assert.That(trace.Hops, Has.Count.EqualTo(3), "trailing silence collapsed into one marker");
        Assert.That(trace.Hops[^1].Status, Is.EqualTo(HopStatus.Timeout));
        Assert.That(trace.Summary, Is.EqualTo("Stopped after hop 2 (100.64.0.1); nothing answered beyond it"));
    }

    [Test]
    public async Task A_silent_hop_in_the_middle_is_kept()
    {
        var probe = new ScriptedProbe(Hop("192.168.1.1"), HopReply.Silent, Hop("10.0.0.1"), Dest("1.1.1.1"));

        var trace = await new Tracer(probe).RunAsync("1.1.1.1", new TraceOptions(), T0);

        Assert.That(trace.Hops.Select(h => h.Address), Is.EqualTo(new[] { "192.168.1.1", null, "10.0.0.1", "1.1.1.1" }));
    }

    [Test]
    public async Task Respects_max_hops()
    {
        var probe = new ScriptedProbe(Enumerable.Range(1, 30).Select(i => Hop($"10.0.0.{i}")).ToArray());

        var trace = await new Tracer(probe).RunAsync("1.1.1.1", new TraceOptions(MaxHops: 5), T0);

        Assert.That(trace.Hops, Has.Count.EqualTo(5));
        Assert.That(trace.Reached, Is.False);
    }

    [Test]
    public async Task Summaries_when_only_the_router_or_nothing_answers()
    {
        var routerOnly = await new Tracer(new ScriptedProbe(Hop("192.168.1.1"))).RunAsync("1.1.1.1", new TraceOptions(), T0);
        var nothing = await new Tracer(new ScriptedProbe()).RunAsync("1.1.1.1", new TraceOptions(), T0);

        Assert.That(routerOnly.Summary, Is.EqualTo("Stopped after your router (192.168.1.1); nothing answered beyond it"));
        Assert.That(nothing.Summary, Is.EqualTo("No hop answered, not even the router"));
        Assert.That(nothing.Hops, Has.Count.EqualTo(1));
    }

    [Test]
    public void Lookup_matches_target_case_insensitively_and_exact_start()
    {
        var trace = new TraceResult("1.1.1.1", T0, []);
        var lookup = new TraceLookup([new OutageTrace("Example.com", T0, trace)]);

        Assert.That(lookup.Find(new Outage("example.com", T0, T0, null, 3)), Is.SameAs(trace));
        Assert.That(lookup.Find(new Outage("example.com", T0.AddSeconds(5), T0, null, 3)), Is.Null);
        Assert.That(lookup.Find(new Outage("other", T0, T0, null, 3)), Is.Null);
    }

    /// <summary>Answers TTL n with the n-th scripted reply, silence after the script ends.</summary>
    private sealed class ScriptedProbe(params HopReply[] replies) : ITraceProbe
    {
        public List<int> Ttls { get; } = [];

        public Task<HopReply> SendAsync(string host, int ttl, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Ttls.Add(ttl);
            return Task.FromResult(ttl <= replies.Length ? replies[ttl - 1] : HopReply.Silent);
        }
    }
}
