using NetPulse.Core.Probing;

namespace NetPulse.Web.Dns;

/// <summary>
/// Runs the DNS resolver comparison on demand ("Compare" on the Speed page or the API), one at a time,
/// and keeps the latest result in memory.
/// </summary>
public sealed class DnsComparisonRunner(ITargetProbe dnsProbe, Func<System.Net.IPAddress?> systemResolver, ILogger<DnsComparisonRunner> logger)
{
    /// <summary>Per-query timeout: a resolver this slow is useless anyway.</summary>
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Raised when a comparison starts or finishes.</summary>
    public event Action? Changed;

    public bool IsRunning { get; private set; }

    public DnsComparison? Latest { get; private set; }

    /// <summary>Runs a comparison; null when one is already running.</summary>
    public async Task<DnsComparison?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            IsRunning = true;
            Changed?.Invoke();

            var resolvers = DnsResolver.WithSystem(systemResolver());
            var comparison = await new DnsComparer(dnsProbe, QueryTimeout)
                .CompareAsync(resolvers, DateTimeOffset.UtcNow, cancellationToken: cancellationToken);

            logger.LogInformation("DNS comparison: {Verdict}", comparison.Verdict);
            Latest = comparison;
            return comparison;
        }
        finally
        {
            IsRunning = false;
            _gate.Release();
            Changed?.Invoke();
        }
    }
}
