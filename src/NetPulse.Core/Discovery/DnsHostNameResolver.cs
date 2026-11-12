using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.Discovery;

/// <summary>Reverse DNS through the system resolver, with a timeout so one slow lookup cannot stall a scan.</summary>
public sealed class DnsHostNameResolver(TimeSpan timeout) : IHostNameResolver
{
    public async Task<string?> ResolveAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var entry = await Dns.GetHostEntryAsync(address.ToString(), cts.Token);
            var name = entry.HostName.TrimEnd('.');

            // Some resolvers answer with the address itself when there is no PTR record.
            return name.Length == 0 || IPAddress.TryParse(name, out _) ? null : name;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }
}
