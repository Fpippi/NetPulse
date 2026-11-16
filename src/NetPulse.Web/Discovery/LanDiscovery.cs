using Microsoft.Extensions.Options;
using NetPulse.Core.Discovery;
using NetPulse.Core.Storage;

namespace NetPulse.Web.Discovery;

/// <summary>State of the discovery, shown on the Devices page.</summary>
/// <param name="Subnet">Last scanned subnet, if any.</param>
/// <param name="LastScan">When the last scan finished.</param>
/// <param name="LastDuration">How long the last scan took.</param>
/// <param name="IsScanning">True while a scan is running.</param>
/// <param name="Error">Why the last scan failed, if it did.</param>
/// <param name="LocalAddress">This machine's address on the scanned network.</param>
/// <param name="Gateway">The default gateway (router) address.</param>
public sealed record DiscoveryStatus(
    string? Subnet,
    DateTimeOffset? LastScan,
    TimeSpan? LastDuration,
    bool IsScanning,
    string? Error,
    string? LocalAddress = null,
    string? Gateway = null)
{
    public static DiscoveryStatus Initial { get; } = new(null, null, null, false, null);
}

/// <summary>
/// Runs a LAN scan and stores the result. Shared by the periodic background service and the
/// "Scan now" button; only one scan runs at a time.
/// </summary>
public sealed class LanDiscovery(
    LanScanner scanner,
    ILanDeviceStore store,
    OuiDatabase vendors,
    IOptions<DiscoveryOptions> options,
    ILogger<LanDiscovery> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusLock = new();
    private DiscoveryStatus _status = DiscoveryStatus.Initial;

    /// <summary>Raised after each scan (successful or not) and when a scan starts.</summary>
    public event Action? Changed;

    public DiscoveryStatus Status
    {
        get { lock (_statusLock) { return _status; } }
    }

    /// <summary>
    /// Scans the network now. If a scan is already running, waits for it instead of starting another one.
    /// </summary>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            // Another scan is running: wait for it to finish and reuse its result.
            await _gate.WaitAsync(cancellationToken);
            _gate.Release();
            return;
        }

        try
        {
            SetStatus(s => s with { IsScanning = true });
            var started = DateTimeOffset.UtcNow;

            try
            {
                var (network, subnet) = ResolveNetwork();
                var opt = options.Value;
                var scanOptions = new LanScanOptions(
                    TimeSpan.FromMilliseconds(Math.Clamp(opt.PingTimeoutMs, 50, 5000)),
                    Math.Clamp(opt.MaxParallelism, 1, 256));

                var observed = await scanner.ScanAsync(network, subnet, scanOptions, cancellationToken);
                var tracker = new DeviceTracker(opt.OfflineAfterMissedScans, vendors.Lookup);
                var update = tracker.Apply(await store.GetDevicesAsync(cancellationToken), observed, DateTimeOffset.UtcNow);
                await store.SaveScanAsync(update, cancellationToken);

                foreach (var e in update.Events)
                {
                    logger.LogInformation("Device {Kind}: {Name} ({Ip})", e.Kind, e.Name, e.Ip);
                }

                var duration = DateTimeOffset.UtcNow - started;
                logger.LogInformation(
                    "Scanned {Subnet} in {Seconds:0.0}s: {Online} devices online",
                    subnet, duration.TotalSeconds, update.Devices.Count(d => d.IsOnline));

                SetStatus(_ => new DiscoveryStatus(
                    subnet.ToString(), DateTimeOffset.UtcNow, duration, false, null,
                    network.Address.ToString(), network.Gateway?.ToString()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "LAN scan failed");
                SetStatus(s => s with { LastScan = DateTimeOffset.UtcNow, IsScanning = false, Error = ex.Message });
            }
            catch (OperationCanceledException)
            {
                SetStatus(s => s with { IsScanning = false });
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private (LocalNetwork Network, Subnet Subnet) ResolveNetwork()
    {
        var opt = options.Value;
        var network = LocalNetwork.Detect()
                      ?? throw new InvalidOperationException("No active IPv4 network interface found.");

        if (!string.IsNullOrWhiteSpace(opt.Subnet))
        {
            return Subnet.TryParse(opt.Subnet, out var configured)
                ? (network, configured!)
                : throw new InvalidOperationException($"'{opt.Subnet}' is not a valid subnet (expected e.g. 192.168.1.0/24).");
        }

        return (network, network.Subnet.LimitTo(Math.Clamp(opt.MaxPrefixLength, 16, 30), network.Address));
    }

    private void SetStatus(Func<DiscoveryStatus, DiscoveryStatus> change)
    {
        lock (_statusLock)
        {
            _status = change(_status);
        }

        Changed?.Invoke();
    }
}
