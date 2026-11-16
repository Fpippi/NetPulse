using Microsoft.Extensions.Options;
using NetPulse.Core.Outages;
using NetPulse.Core.Storage;
using NetPulse.Core.Traceroute;

namespace NetPulse.Web.Monitoring;

/// <summary>
/// Records the route towards a target when one of its outages starts, so the history (and the ISP
/// report) can show where the path was cut: at your router, at the first hop of the ISP, or further away.
/// Traces run one at a time in the background and never delay the monitoring cycle.
/// </summary>
public sealed class OutageTracer(
    Tracer tracer,
    IOutageTraceStore store,
    IOptions<OutageOptions> options,
    ILogger<OutageTracer> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Starts a traceroute for the outage if it makes sense; returns immediately.</summary>
    public void OnOutageStarted(Outage outage, CancellationToken stoppingToken)
    {
        if (!options.Value.TraceRoute || !Tracer.IsTraceable(outage.Target, MonitorOptions.GatewayTarget))
        {
            return;
        }

        _ = Task.Run(() => TraceAsync(outage, stoppingToken), stoppingToken);
    }

    private async Task TraceAsync(Outage outage, CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct);
            try
            {
                var opt = options.Value;
                var traceOptions = new TraceOptions(Math.Clamp(opt.TraceMaxHops, 1, 64), TimeSpan.FromSeconds(1));
                var result = await tracer.RunAsync(outage.Target, traceOptions, DateTimeOffset.UtcNow, ct);
                await store.SaveAsync(new OutageTrace(outage.Target, outage.Start, result), ct);
                logger.LogInformation("Traceroute to {Target} during outage: {Summary}", outage.Target, result.Summary);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Traceroute to {Target} failed", outage.Target);
        }
    }
}
