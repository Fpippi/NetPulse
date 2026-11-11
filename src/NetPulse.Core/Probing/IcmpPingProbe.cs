using System.Net.NetworkInformation;

namespace NetPulse.Core.Probing;

/// <summary>
/// ICMP echo probe based on <see cref="Ping"/>.
/// </summary>
public sealed class IcmpPingProbe : IPingProbe
{
    public async Task<PingResult> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, timeout, cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success
                ? PingResult.Ok(reply.RoundtripTime)
                : PingResult.Failed(reply.Status.ToString());
        }
        catch (PingException ex)
        {
            return PingResult.Failed(ex.InnerException?.Message ?? ex.Message);
        }
    }
}
