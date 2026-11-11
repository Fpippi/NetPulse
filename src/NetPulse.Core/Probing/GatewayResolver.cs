using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetPulse.Core.Probing;

public static class GatewayResolver
{
    /// <summary>
    /// Returns the IPv4 default gateway of the first active, non-loopback interface, or null if none is found.
    /// </summary>
    public static IPAddress? GetDefaultGateway() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
}
