using Microsoft.Extensions.Options;
using NetPulse.Core.Connection;
using NetPulse.Core.Storage;
using NetPulse.Web.Monitoring;

namespace NetPulse.Web.Connection;

/// <summary>
/// Watches how this machine is connected (Wi-Fi or cable, network, signal) every 30 seconds, keeps the
/// latest reading for the dashboard and records one sample a minute (and every change), so outages can
/// later be matched with the link they happened on.
/// </summary>
public sealed class LinkMonitor(
    LinkDetector detector,
    ILinkStore store,
    IOptions<MonitorOptions> options,
    ILogger<LinkMonitor> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StoreEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PurgeEvery = TimeSpan.FromHours(1);

    private volatile LinkInfo? _current;

    /// <summary>The latest reading; null before the first one.</summary>
    public LinkInfo? Current => _current;

    public event Action? Changed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastStored = DateTimeOffset.MinValue;
        var lastPurge = DateTimeOffset.MinValue;
        LinkInfo? previous = null;

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var link = await detector.DetectAsync(stoppingToken);
                _current = link;
                Changed?.Invoke();

                var switched = previous is null
                    || previous.Type != link.Type || previous.Interface != link.Interface || previous.Ssid != link.Ssid;
                if (switched && previous is not null)
                {
                    logger.LogInformation("Connection changed from {Before} to {After} ({Interface})",
                        previous.TypeLabel, link.TypeLabel, link.Interface);
                }
                else if (previous is null)
                {
                    logger.LogInformation("Connected via {Type} ({Interface})", link.TypeLabel, link.Interface);
                }

                if (switched || now - lastStored >= StoreEvery)
                {
                    await store.AddAsync(new LinkSample(now, link), stoppingToken);
                    lastStored = now;
                }

                previous = link;

                if (now - lastPurge >= PurgeEvery)
                {
                    // Kept as long as the samples it explains.
                    await store.PurgeOlderThanAsync(now.AddDays(-Math.Max(1, options.Value.RetentionDays)), stoppingToken);
                    lastPurge = now;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reading the network link failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
