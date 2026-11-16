using System.Net.NetworkInformation;

namespace NetPulse.Core.Traceroute;

/// <summary>Traceroute probe based on <see cref="Ping"/> with a limited TTL.</summary>
public sealed class IcmpTraceProbe : ITraceProbe
{
    private static readonly byte[] Payload = new byte[32];

    public async Task<HopReply> SendAsync(string host, int ttl, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, timeout, Payload, new PingOptions(ttl, dontFragment: true), cancellationToken);
            return reply.Status switch
            {
                IPStatus.Success => new HopReply(HopStatus.Reached, reply.Address, reply.RoundtripTime),
                IPStatus.TtlExpired or IPStatus.TimeExceeded => new HopReply(HopStatus.TtlExpired, reply.Address, reply.RoundtripTime),
                IPStatus.TimedOut => HopReply.Silent,
                _ => new HopReply(HopStatus.Error, null, null),
            };
        }
        catch (PingException)
        {
            return new HopReply(HopStatus.Error, null, null);
        }
    }
}
