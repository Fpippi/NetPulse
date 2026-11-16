using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetPulse.Core.Probing;

public static class DnsServerResolver
{
    /// <summary>
    /// Returns the first IPv4 DNS server configured on an active, non-loopback interface (usually the
    /// one the router hands out), or null if none is found. On Linux with systemd-resolved this can be
    /// the local stub (127.0.0.53); name a server explicitly (<c>dns:name@server</c>) to bypass it.
    /// </summary>
    public static IPAddress? GetDefaultDnsServer() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().DnsAddresses)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
}
