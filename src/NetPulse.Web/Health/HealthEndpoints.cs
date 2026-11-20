using NetPulse.Core.Metrics;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;

namespace NetPulse.Web.Health;

/// <summary><c>/healthz</c> and <c>/metrics</c>, outside <c>/api</c> where monitoring tools expect them.</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Health for Docker, Uptime Kuma & co.: the ping loop is alive and the database readable.
        app.MapHealthChecks("/healthz").AllowAnonymous();

        // Prometheus metrics (Grafana, Home Assistant): current state, latency stats over the last 5 minutes.
        app.MapGet("/metrics", async (
            ISampleStore samples, IOutageStore outages, ISpeedTestStore speedTests, ILanDeviceStore devices,
            TargetManager targets, CancellationToken ct) =>
        {
            var window = TimeSpan.FromMinutes(5);
            var now = DateTimeOffset.UtcNow;
            var snapshot = new MetricsSnapshot(
                window,
                await samples.GetSinceAsync(now - window, cancellationToken: ct),
                await targets.GetEffectiveTargetsAsync(ct),
                (await outages.GetSinceAsync(now, ct)).Where(o => o.IsOngoing).ToList(),
                await speedTests.GetLatestAsync(ct),
                await devices.GetDevicesAsync(ct));

            return Results.Text(NetPulseMetrics.Render(snapshot), PrometheusWriter.ContentType);
        });

        return app;
    }
}
