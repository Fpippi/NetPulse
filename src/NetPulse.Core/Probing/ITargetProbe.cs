namespace NetPulse.Core.Probing;

/// <summary>Measures one target of a given <see cref="ProbeKind"/>.</summary>
public interface ITargetProbe
{
    Task<PingResult> ProbeAsync(ProbeTarget target, TimeSpan timeout, CancellationToken cancellationToken = default);
}
