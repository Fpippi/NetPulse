namespace NetPulse.Core.Probing;

/// <summary>Timeouts per probe kind: a DNS or HTTPS round trip legitimately takes longer than a ping.</summary>
public sealed record ProbeTimeouts(TimeSpan Ping, TimeSpan Dns, TimeSpan Http)
{
    public TimeSpan For(ProbeKind kind) => kind switch
    {
        ProbeKind.Dns => Dns,
        ProbeKind.Http => Http,
        _ => Ping,
    };
}

/// <summary>Sends each target to the probe that matches its <see cref="ProbeKind"/>.</summary>
public sealed class TargetProbe(IPingProbe ping, ITargetProbe dns, ITargetProbe http, ProbeTimeouts timeouts)
{
    public ProbeTimeouts Timeouts => timeouts;

    public Task<PingResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken = default)
    {
        var timeout = timeouts.For(target.Kind);
        return target.Kind switch
        {
            ProbeKind.Dns => dns.ProbeAsync(target, timeout, cancellationToken),
            ProbeKind.Http => http.ProbeAsync(target, timeout, cancellationToken),
            _ => ping.PingAsync(target.Host, timeout, cancellationToken),
        };
    }
}
