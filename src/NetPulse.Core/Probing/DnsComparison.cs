using System.Net;

namespace NetPulse.Core.Probing;

/// <summary>A DNS resolver to compare.</summary>
/// <param name="Name">Display name, e.g. "Cloudflare".</param>
/// <param name="Address">Server address (UDP port 53).</param>
/// <param name="IsSystem">True for the resolver this machine uses now (usually the router's or the ISP's).</param>
public sealed record DnsResolver(string Name, IPAddress Address, bool IsSystem = false)
{
    /// <summary>Free public resolvers that anyone can switch to.</summary>
    public static IReadOnlyList<DnsResolver> Public { get; } =
    [
        new("Cloudflare", IPAddress.Parse("1.1.1.1")),
        new("Google", IPAddress.Parse("8.8.8.8")),
        new("Quad9", IPAddress.Parse("9.9.9.9")),
        new("OpenDNS", IPAddress.Parse("208.67.222.222")),
    ];

    /// <summary>
    /// The system resolver first (when known and not a local stub such as 127.0.0.53), then the public
    /// ones; a public resolver that is already the system one is listed once, as the system one.
    /// </summary>
    public static IReadOnlyList<DnsResolver> WithSystem(IPAddress? system)
    {
        if (system is null || IPAddress.IsLoopback(system))
        {
            return Public;
        }

        var known = Public.FirstOrDefault(r => r.Address.Equals(system));
        var mine = new DnsResolver(known is null ? "Your resolver" : $"{known.Name} (yours)", system, IsSystem: true);
        return [mine, .. Public.Where(r => !r.Address.Equals(system))];
    }
}

/// <summary>
/// How a query is expected to be answered: <see cref="Cached"/> names are popular and already in the
/// resolver's cache (what most page loads hit); <see cref="Uncached"/> names have a random label, so the
/// resolver has to ask the domain's own servers (a first visit).
/// </summary>
public enum DnsQueryKind
{
    Cached,
    Uncached,
}

/// <summary>One timed query; <paramref name="LatencyMs"/> is null when it failed or timed out.</summary>
public sealed record DnsMeasurement(DnsResolver Resolver, DnsQueryKind Kind, string Name, double? LatencyMs);

/// <summary>Results of one resolver.</summary>
/// <param name="Queries">Queries that counted (warm-up queries excluded).</param>
/// <param name="Failed">Queries without an answer.</param>
/// <param name="CachedMedianMs">Median of the answered cached queries.</param>
/// <param name="UncachedMedianMs">Median of the answered uncached queries.</param>
/// <param name="MedianMs">Median of all answered queries, the ranking key.</param>
public sealed record DnsResolverScore(
    DnsResolver Resolver,
    int Queries,
    int Failed,
    double? CachedMedianMs,
    double? UncachedMedianMs,
    double? MedianMs)
{
    /// <summary>Resolvers failing more often than this are ranked after the reliable ones.</summary>
    public const double MaxFailurePercent = 10;

    public double FailurePercent => Queries == 0 ? 100 : 100.0 * Failed / Queries;

    public bool IsReliable => Queries > 0 && MedianMs is not null && FailurePercent <= MaxFailurePercent;
}

/// <summary>The resolvers ranked from fastest to slowest; unreliable ones last.</summary>
public sealed record DnsComparison(DateTimeOffset Timestamp, IReadOnlyList<DnsResolverScore> Ranking)
{
    /// <summary>Differences smaller than this are not worth switching resolvers for.</summary>
    public const double NoticeableMs = 5;

    public DnsResolverScore? Fastest => Ranking.FirstOrDefault(r => r.IsReliable);

    public DnsResolverScore? System => Ranking.FirstOrDefault(r => r.Resolver.IsSystem);

    /// <summary>How much faster the fastest resolver answers than the system one (median), when both are known.</summary>
    public double? SavingMs => Fastest?.MedianMs is { } fastest && System is { IsReliable: true, MedianMs: { } mine }
        ? mine - fastest
        : null;

    /// <summary>One sentence for the page, e.g. "Cloudflare answers 18 ms faster than your resolver."</summary>
    public string Verdict
    {
        get
        {
            if (Fastest is not { } fastest)
            {
                return "No resolver answered reliably. Check that UDP port 53 is not blocked.";
            }

            if (System is not { } mine)
            {
                return $"{fastest.Resolver.Name} was the fastest.";
            }

            if (!mine.IsReliable)
            {
                return $"Your resolver did not answer reliably; {fastest.Resolver.Name} did.";
            }

            if (fastest.Resolver.IsSystem)
            {
                return "Your resolver is already the fastest.";
            }

            return SavingMs is { } saving && saving >= NoticeableMs
                ? $"{fastest.Resolver.Name} answers {Math.Round(saving):0} ms faster than your resolver."
                : $"Your resolver is about as fast as the fastest ({fastest.Resolver.Name}): no need to switch.";
        }
    }
}

/// <summary>
/// Compares DNS resolvers from this network: every resolver gets the same queries (popular names that
/// are cached everywhere, and random names that must be looked up), and is ranked by its median answer
/// time. Resolvers run in parallel, the queries of one resolver one after another.
/// </summary>
public sealed class DnsComparer(ITargetProbe probe, TimeSpan timeout)
{
    /// <summary>Popular domains, cached by every large resolver.</summary>
    public static IReadOnlyList<string> PopularNames { get; } =
    [
        "google.com", "youtube.com", "wikipedia.org", "amazon.com",
        "netflix.com", "github.com", "whatsapp.net", "microsoft.com",
    ];

    /// <summary>Rounds of cached queries per name, after one warm-up query that does not count.</summary>
    public const int CachedRounds = 2;

    /// <summary>
    /// The queries of one run: a warm-up query per popular name (not counted), then
    /// <see cref="CachedRounds"/> rounds of them, then one name with a random label under each domain.
    /// </summary>
    public static IReadOnlyList<(DnsQueryKind Kind, string Name, bool Counted)> Plan(Random random)
    {
        var plan = new List<(DnsQueryKind, string, bool)>();
        plan.AddRange(PopularNames.Select(n => (DnsQueryKind.Cached, n, false)));
        for (var round = 0; round < CachedRounds; round++)
        {
            plan.AddRange(PopularNames.Select(n => (DnsQueryKind.Cached, n, true)));
        }

        plan.AddRange(PopularNames.Select(n => (DnsQueryKind.Uncached, $"np-{RandomLabel(random)}.{n}", true)));
        return plan;
    }

    public async Task<DnsComparison> CompareAsync(
        IReadOnlyList<DnsResolver> resolvers, DateTimeOffset now, Random? random = null, CancellationToken cancellationToken = default)
    {
        var plan = Plan(random ?? Random.Shared);
        var perResolver = await Task.WhenAll(resolvers.Select(r => MeasureAsync(r, plan, cancellationToken)));
        return Score(perResolver.SelectMany(m => m), resolvers, now);
    }

    private async Task<List<DnsMeasurement>> MeasureAsync(
        DnsResolver resolver, IReadOnlyList<(DnsQueryKind Kind, string Name, bool Counted)> plan, CancellationToken ct)
    {
        var results = new List<DnsMeasurement>();
        foreach (var (kind, name, counted) in plan)
        {
            var target = new ProbeTarget($"dns:{name}@{resolver.Address}", ProbeKind.Dns, name, DnsServer: resolver.Address);
            var result = await probe.ProbeAsync(target, timeout, ct);
            if (counted)
            {
                results.Add(new DnsMeasurement(resolver, kind, name, result.Success ? result.LatencyMs : null));
            }
        }

        return results;
    }

    /// <summary>Ranks the resolvers: reliable ones by median, then the others by failure rate.</summary>
    public static DnsComparison Score(IEnumerable<DnsMeasurement> measurements, IReadOnlyList<DnsResolver> resolvers, DateTimeOffset now)
    {
        var byResolver = measurements.ToLookup(m => m.Resolver);
        var scores = resolvers.Select(resolver =>
        {
            var all = byResolver[resolver].ToList();
            double? Median(DnsQueryKind? kind) => MedianOf(all
                .Where(m => kind is null || m.Kind == kind)
                .Where(m => m.LatencyMs.HasValue)
                .Select(m => m.LatencyMs!.Value));

            return new DnsResolverScore(
                resolver,
                all.Count,
                all.Count(m => m.LatencyMs is null),
                Median(DnsQueryKind.Cached),
                Median(DnsQueryKind.Uncached),
                Median(null));
        });

        var ranking = scores
            .OrderBy(s => s.IsReliable ? 0 : 1)
            .ThenBy(s => s.IsReliable ? s.MedianMs : s.FailurePercent)
            .ThenBy(s => s.Resolver.IsSystem ? 0 : 1)
            .ToList();

        return new DnsComparison(now, ranking);
    }

    public static double? MedianOf(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return null;
        }

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static string RandomLabel(Random random)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return string.Create(10, random, (span, r) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[r.Next(chars.Length)];
            }
        });
    }
}
