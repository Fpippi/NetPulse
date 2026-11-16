using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetPulse.Core.Discovery;

/// <summary>The local network this machine is connected to.</summary>
/// <param name="Address">This machine's IPv4 address on the network.</param>
/// <param name="Subnet">The network to scan.</param>
/// <param name="Mac">This machine's MAC address, if known.</param>
/// <param name="Gateway">The default gateway, if any.</param>
public sealed record LocalNetwork(IPAddress Address, Subnet Subnet, string? Mac, IPAddress? Gateway)
{
    /// <summary>
    /// Picks the active interface that has an IPv4 address and, preferably, a default gateway
    /// (that is the one connected to the LAN, not a VPN or a virtual switch). Returns null if none is found.
    /// </summary>
    public static LocalNetwork? Detect()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(ToLocalNetwork)
            .OfType<LocalNetwork>()
            .ToList();

        return candidates.FirstOrDefault(c => c.Gateway is not null) ?? candidates.FirstOrDefault();
    }

    private static LocalNetwork? ToLocalNetwork(NetworkInterface nic)
    {
        var properties = nic.GetIPProperties();
        var unicast = properties.UnicastAddresses.FirstOrDefault(a =>
            a.Address.AddressFamily == AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(a.Address)
            && a.PrefixLength is > 0 and < 32);

        if (unicast is null)
        {
            return null;
        }

        var gateway = properties.GatewayAddresses
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));

        var mac = MacAddress.TryNormalize(nic.GetPhysicalAddress().ToString(), out var m) && MacAddress.IsUnicastDevice(m)
            ? m
            : null;

        return new LocalNetwork(unicast.Address, Subnet.Create(unicast.Address, unicast.PrefixLength), mac, gateway);
    }
}
