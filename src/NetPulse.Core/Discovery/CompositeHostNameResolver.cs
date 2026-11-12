using System.Net;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Asks several resolvers in turn and returns the first name found, so reverse DNS, NetBIOS and mDNS
/// together name more devices than any of them alone.
/// </summary>
public sealed class CompositeHostNameResolver(params IHostNameResolver[] resolvers) : IHostNameResolver
{
    public async Task<string?> ResolveAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        foreach (var resolver in resolvers)
        {
            var name = HostName.Clean(await resolver.ResolveAsync(address, cancellationToken));
            if (name is not null)
            {
                return name;
            }
        }

        return null;
    }
}
