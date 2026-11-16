using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Resolves names over NetBIOS (UDP 137). Windows machines and Samba servers answer with their computer
/// name, which is often missing from DNS. Returns null on timeout or any network error.
/// </summary>
public sealed class NetBiosNameResolver(TimeSpan timeout) : IHostNameResolver
{
    private const int NetBiosPort = 137;

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
            var request = NetBiosName.BuildNodeStatusRequest((ushort)Random.Shared.Next(ushort.MaxValue));
            await udp.SendAsync(request, new IPEndPoint(address, NetBiosPort), cts.Token);

            var reply = await udp.ReceiveAsync(cts.Token);
            return NetBiosName.TryParseNodeStatusResponse(reply.Buffer);
        }
        catch (Exception ex) when (ex is SocketException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }
}
