using System.Net;
using NetPulse.Core.Probing;

namespace NetPulse.Tests.Probing;

[TestFixture]
public class DnsComparisonTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Answers with a fixed latency per server (cached / uncached), or fails for listed servers.</summary>
    private sealed class FakeProbe(Dictionary<string, (double Cached, double Uncached)> latency, HashSet<string>? failing = null) : ITargetProbe
    {
        public List<ProbeTarget> Queries { get; } = [];

        public Task<PingResult> ProbeAsync(ProbeTarget target, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            lock (Queries)
            {
                Queries.Add(target);
            }

            var server = target.DnsServer!.ToString();
            if (failing?.Contains(server) == true || !latency.TryGetValue(server, out var ms))
            {
                return Task.FromResult(PingResult.Failed("TimedOut"));
            }

            return Task.FromResult(PingResult.Ok(target.Host.StartsWith("np-", StringComparison.Ordinal) ? ms.Uncached : ms.Cached));
        }
    }

    [Test]
    public void The_system_resolver_comes_first_and_is_not_repeated()
    {
        var list = DnsResolver.WithSystem(IPAddress.Parse("8.8.8.8"));

        Assert.That(list[0], Is.EqualTo(new DnsResolver("Google (yours)", IPAddress.Parse("8.8.8.8"), true)));
        Assert.That(list.Count(r => r.Address.Equals(IPAddress.Parse("8.8.8.8"))), Is.EqualTo(1));
        Assert.That(list, Has.Count.EqualTo(DnsResolver.Public.Count));
    }

    [Test]
    public void A_local_stub_or_unknown_system_resolver_is_left_out()
    {
        Assert.That(DnsResolver.WithSystem(IPAddress.Parse("127.0.0.53")), Is.EqualTo(DnsResolver.Public));
        Assert.That(DnsResolver.WithSystem(null), Is.EqualTo(DnsResolver.Public));
        Assert.That(DnsResolver.WithSystem(IPAddress.Parse("192.168.1.1"))[0].Name, Is.EqualTo("Your resolver"));
    }

    [Test]
    public void The_plan_warms_up_then_counts_cached_and_uncached_queries()
    {
        var plan = DnsComparer.Plan(new Random(1));
        var names = DnsComparer.PopularNames.Count;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Take(names).All(q => !q.Counted), Is.True);
            Assert.That(plan.Count(q => q.Counted && q.Kind == DnsQueryKind.Cached), Is.EqualTo(names * DnsComparer.CachedRounds));
            Assert.That(plan.Where(q => q.Kind == DnsQueryKind.Uncached).Select(q => q.Name).Distinct().Count(), Is.EqualTo(names));
            Assert.That(plan.Where(q => q.Kind == DnsQueryKind.Uncached).All(q => q.Name.StartsWith("np-", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task Ranks_by_median_and_measures_every_resolver_with_the_same_queries()
    {
        var probe = new FakeProbe(new()
        {
            ["192.168.1.1"] = (30, 80),
            ["1.1.1.1"] = (8, 40),
            ["8.8.8.8"] = (12, 45),
            ["9.9.9.9"] = (15, 60),
            ["208.67.222.222"] = (20, 70),
        });
        var resolvers = DnsResolver.WithSystem(IPAddress.Parse("192.168.1.1"));

        var comparison = await new DnsComparer(probe, TimeSpan.FromSeconds(1)).CompareAsync(resolvers, Now, new Random(3));

        Assert.Multiple(() =>
        {
            Assert.That(comparison.Ranking.Select(r => r.Resolver.Name),
                Is.EqualTo(new[] { "Cloudflare", "Google", "Quad9", "OpenDNS", "Your resolver" }));
            Assert.That(comparison.Fastest!.CachedMedianMs, Is.EqualTo(8));
            Assert.That(comparison.Fastest.UncachedMedianMs, Is.EqualTo(40));
            Assert.That(comparison.Fastest.Queries, Is.EqualTo(24));
            Assert.That(comparison.SavingMs, Is.EqualTo(22));
            Assert.That(comparison.Verdict, Is.EqualTo("Cloudflare answers 22 ms faster than your resolver."));
            Assert.That(probe.Queries, Has.Count.EqualTo(5 * 32));
        });
    }

    [Test]
    public async Task Unreliable_resolvers_go_last()
    {
        var probe = new FakeProbe(new() { ["1.1.1.1"] = (8, 40), ["8.8.8.8"] = (12, 45) }, failing: ["1.1.1.1"]);

        var comparison = await new DnsComparer(probe, TimeSpan.FromSeconds(1)).CompareAsync(DnsResolver.Public, Now);

        Assert.That(comparison.Ranking[0].Resolver.Name, Is.EqualTo("Google"));
        Assert.That(comparison.Ranking.Skip(1).All(r => !r.IsReliable), Is.True);
        Assert.That(comparison.Ranking.First(r => r.Resolver.Name == "Cloudflare").FailurePercent, Is.EqualTo(100));
    }

    [Test]
    public void Verdicts()
    {
        var mine = new DnsResolver("Your resolver", IPAddress.Parse("192.168.1.1"), true);
        var cloudflare = DnsResolver.Public[0];
        DnsResolverScore Score(DnsResolver r, double median, int failed = 0) => new(r, 24, failed, median, median, median);

        Assert.Multiple(() =>
        {
            Assert.That(new DnsComparison(Now, [Score(mine, 10), Score(cloudflare, 12)]).Verdict,
                Is.EqualTo("Your resolver is already the fastest."));
            Assert.That(new DnsComparison(Now, [Score(cloudflare, 10), Score(mine, 13)]).Verdict,
                Does.StartWith("Your resolver is about as fast as the fastest (Cloudflare)"));
            Assert.That(new DnsComparison(Now, [Score(cloudflare, 10), Score(mine, 13, failed: 12)]).Verdict,
                Is.EqualTo("Your resolver did not answer reliably; Cloudflare did."));
            Assert.That(new DnsComparison(Now, [Score(cloudflare, 10, failed: 24)]).Verdict,
                Does.StartWith("No resolver answered reliably"));
            Assert.That(new DnsComparison(Now, [Score(cloudflare, 10)]).Verdict, Is.EqualTo("Cloudflare was the fastest."));
        });
    }

    [TestCase(new double[] { }, null)]
    [TestCase(new double[] { 5 }, 5.0)]
    [TestCase(new double[] { 9, 1, 5 }, 5.0)]
    [TestCase(new double[] { 1, 2, 3, 10 }, 2.5)]
    public void Median(double[] values, double? expected) =>
        Assert.That(DnsComparer.MedianOf(values), Is.EqualTo(expected));
}
