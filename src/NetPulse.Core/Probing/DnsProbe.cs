using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetPulse.Core.Discovery;

namespace NetPulse.Core.Probing;

/// <summary>
/// Times a single A-record query over UDP, like <c>dnsping</c>. This measures how fast the resolver
/// answers (usually from its cache), which is what makes browsing feel slow when it degrades; the
/// system resolver cache is bypassed because the query is sent directly to the server.
/// </summary>
/// <param name="defaultServer">Server used when the target names none, e.g. the one from DHCP.</param>
/// <param name="port">Server port; only tests use anything but 53.</param>
public sealed class DnsProbe(Func<IPAddress?> defaultServer, int port = 53) : ITargetProbe
{
    public DnsProbe() : this(DnsServerResolver.GetDefaultDnsServer)
    {
    }

    public async Task<PingResult> ProbeAsync(ProbeTarget target, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var server = target.DnsServer ?? defaultServer();
        if (server is null)
        {
            return PingResult.Failed("No DNS server configured");
        }

        var id = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        var query = DnsWire.BuildAddressQuery(target.Host, id);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        using var udp = new UdpClient(server.AddressFamily);

        try
        {
            udp.Connect(server, port);
            var stopwatch = Stopwatch.StartNew();
            await udp.SendAsync(query, cts.Token);

            while (true)
            {
                var received = await udp.ReceiveAsync(cts.Token);
                if (!DnsWire.TryReadResponseCode(received.Buffer, id, out var code))
                {
                    continue; // a stray or late datagram: keep waiting for ours
                }

                var elapsed = stopwatch.Elapsed.TotalMilliseconds;

                // NXDOMAIN is still a valid answer from a working resolver.
                return code is DnsWire.ResponseCode.NoError or DnsWire.ResponseCode.NameError
                    ? PingResult.Ok(Math.Round(elapsed, 1))
                    : PingResult.Failed($"DNS {code}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PingResult.Failed("TimedOut");
        }
        catch (SocketException ex)
        {
            return PingResult.Failed(ex.SocketErrorCode.ToString());
        }
    }
}
