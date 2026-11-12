using System.Net;

namespace NetPulse.Core.Discovery;

/// <summary>Finds the host name of an address (reverse DNS).</summary>
public interface IHostNameResolver
{
    /// <summary>Returns the host name, or null if there is none or the lookup fails.</summary>
    Task<string?> ResolveAsync(IPAddress address, CancellationToken cancellationToken = default);
}
