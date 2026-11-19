using Microsoft.Extensions.Options;
using NetPulse.Core.Notifications;
using NetPulse.Core.PublicIp;
using NetPulse.Core.Storage;
using NetPulse.Core.Traceroute;

namespace NetPulse.Web.PublicIp;

/// <summary>
/// Reads the public IP address every few minutes and stores each change, so the Outages page can show
/// when the modem reconnected. Once a day it also traces the first hops towards the Internet to spot
/// carrier-grade NAT.
/// </summary>
public sealed class PublicIpMonitor(
    IHttpClientFactory httpClientFactory,
    IPublicIpStore store,
    Tracer tracer,
    INotificationPublisher notifications,
    IOptions<PublicIpOptions> options,
    ILogger<PublicIpMonitor> logger) : BackgroundService
{
    public const string HttpClientName = "PublicIp";

    /// <summary>Target of the CGNAT trace; the hops before it are what matters.</summary>
    private const string TraceHost = "1.1.1.1";

    private static readonly TimeSpan CgnatInterval = TimeSpan.FromDays(1);

    /// <summary>The latest stored address, null before the first successful reading.</summary>
    public PublicIpChange? Current { get; private set; }

    /// <summary>When the address was last read successfully.</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>Why the last reading failed, null when it worked.</summary>
    public string? LastError { get; private set; }

    /// <summary>Outcome of the last carrier-grade NAT check, null before the first one (or when it is off).</summary>
    public CgnatStatus? Cgnat { get; private set; }

    public bool Enabled => options.Value.Enabled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        Current = await store.GetLatestAsync(stoppingToken);
        var lastCgnatCheck = DateTimeOffset.MinValue;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)));
        do
        {
            await CheckAddressAsync(stoppingToken);

            if (options.Value.CgnatCheck && DateTimeOffset.UtcNow - lastCgnatCheck >= CgnatInterval)
            {
                lastCgnatCheck = DateTimeOffset.UtcNow;
                await CheckCgnatAsync(stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAddressAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var http = httpClientFactory.CreateClient(HttpClientName);
            var body = await http.GetStringAsync(options.Value.Url, timeout.Token);

            if (!PublicIpResponse.TryParse(body, out var address))
            {
                LastError = $"{options.Value.Url} did not return a public address.";
                logger.LogWarning("Public IP: {Url} did not return a public address", options.Value.Url);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            LastChecked = now;
            LastError = null;

            var text = address.ToString();
            if (Current is { } current && current.Address == text)
            {
                return;
            }

            var change = new PublicIpChange(now, text, Current?.Address);
            await store.AddAsync(change, ct);
            Current = change;

            if (change.IsFirstReading)
            {
                logger.LogInformation("Public IP is {Address}", text);
            }
            else
            {
                logger.LogInformation("Public IP changed from {Previous} to {Address}", change.Previous, text);
            }

            if (NotificationFactory.PublicIpChanged(change) is { } notification)
            {
                notifications.Publish(notification);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Expected while the Internet is down: keep the last known address and try again later.
            LastError = ex.Message;
            logger.LogDebug(ex, "Reading the public IP failed");
        }
    }

    private async Task CheckCgnatAsync(CancellationToken ct)
    {
        try
        {
            var trace = await tracer.RunAsync(
                TraceHost, new TraceOptions(CgnatCheck.HopsToInspect, TimeSpan.FromSeconds(1)), DateTimeOffset.UtcNow, ct);
            Cgnat = CgnatStatus.From(trace);
            if (Cgnat.BehindCgnat)
            {
                logger.LogInformation("Carrier-grade NAT detected at hop {Hop} ({Address})",
                    Cgnat.CgnatHop!.Number, Cgnat.CgnatHop.Address);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Carrier-grade NAT check failed");
        }
    }
}
