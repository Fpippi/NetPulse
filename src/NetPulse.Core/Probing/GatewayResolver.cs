using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetPulse.Core.Probing;

public static class GatewayResolver
{
    /// <summary>
    /// Returns the default gateway of <paramref name="family"/> (IPv4 by default) of the first active,
    /// non-loopback interface, or null if none is found. IPv6 routers are usually link-local addresses;
    /// they come back with their scope (interface) id, e.g. <c>fe80::1%2</c>, so they can be pinged as is.
    /// </summary>
    public static IPAddress? GetDefaultGateway(AddressFamily family = AddressFamily.InterNetwork) =>
        ActiveInterfaces()
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == family
                                 && !a.Equals(IPAddress.Any)
                                 && !a.Equals(IPAddress.IPv6Any));

    /// <summary>
    /// True when an active interface has a global IPv6 address, i.e. the network hands out IPv6.
    /// </summary>
    public static bool HasGlobalIpv6Address() =>
        ActiveInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(u => IsGlobalIpv6(u.Address));

    /// <summary>
    /// True for an IPv6 address reachable on the Internet: not loopback, link-local, site-local,
    /// unique-local (fc00::/7), multicast, IPv4-mapped or documentation (2001:db8::/32).
    /// </summary>
    public static bool IsGlobalIpv6(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6UniqueLocal
            || address.IsIPv6Multicast
            || address.IsIPv4MappedToIPv6
            || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        var documentation = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;

        // Global unicast is 2000::/3.
        return !documentation && (bytes[0] & 0xE0) == 0x20;
    }

    private static IEnumerable<NetworkInterface> ActiveInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
}
