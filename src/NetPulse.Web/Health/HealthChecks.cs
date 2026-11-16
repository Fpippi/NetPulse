using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NetPulse.Core.Metrics;
using NetPulse.Core.Storage;
using NetPulse.Web.Monitoring;

namespace NetPulse.Web.Health;

/// <summary>Unhealthy when the ping loop has not completed a cycle for a while.</summary>
public sealed class MonitorHealthCheck(MonitorHeartbeat heartbeat, IOptions<MonitorOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        var age = now - heartbeat.LastBeat;

        return Task.FromResult(heartbeat.IsAlive(now, interval)
            ? HealthCheckResult.Healthy($"Last monitoring cycle {age.TotalSeconds:0} s ago")
            : HealthCheckResult.Unhealthy($"No monitoring cycle for {age.TotalSeconds:0} s"));
    }
}

/// <summary>Unhealthy when the SQLite database cannot be read.</summary>
public sealed class DatabaseHealthCheck(ISampleStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await store.GetSinceAsync(DateTimeOffset.UtcNow, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("Database readable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database not readable", ex);
        }
    }
}
