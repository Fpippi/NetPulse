using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.Probing;

/// <summary>Looks up the addresses of a host name in one IP family (for <c>ping4:</c> / <c>ping6:</c>).</summary>
public interface IHostResolver
{
    /// <summary>The first address of <paramref name="family"/>, or null when the name has none.</summary>
    Task<IPAddress?> ResolveAsync(string host, AddressFamily family, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IHostResolver"/> backed by the operating system's resolver.</summary>
public sealed class SystemHostResolver : IHostResolver
{
    public async Task<IPAddress?> ResolveAsync(string host, AddressFamily family, CancellationToken cancellationToken = default)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, family, cancellationToken);
            return addresses.FirstOrDefault(a => a.AddressFamily == family);
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
