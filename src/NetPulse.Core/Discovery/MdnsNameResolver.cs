using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Resolves names over multicast DNS (mDNS, UDP 5353). Apple, Linux (Avahi) and many IoT devices answer
/// a reverse lookup with their <c>name.local</c> host name. Returns null on timeout or any network error.
/// </summary>
public sealed class MdnsNameResolver(TimeSpan timeout) : IHostNameResolver
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);

    public async Task<string?> ResolveAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var query = DnsWire.BuildReversePtrQuery(address, transactionId: 0); // mDNS ignores the id
            await udp.SendAsync(query, MulticastEndpoint, cts.Token);

            // Several hosts may answer on the multicast group; keep only the one for our query.
            while (true)
            {
                var reply = await udp.ReceiveAsync(cts.Token);
                var name = DnsWire.TryReadFirstPtrTarget(reply.Buffer);
                if (name is not null)
                {
                    return name;
                }
            }
        }
        catch (Exception ex) when (ex is SocketException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }
}
