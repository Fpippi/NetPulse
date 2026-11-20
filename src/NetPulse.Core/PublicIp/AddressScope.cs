using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.PublicIp;

/// <summary>Where an IP address can be reached from.</summary>
public enum AddressScopeKind
{
    /// <summary>Routable on the Internet.</summary>
    Public,

    /// <summary>A home or company network: 10/8, 172.16/12, 192.168/16, fc00::/7.</summary>
    Private,

    /// <summary>100.64.0.0/10 (RFC 6598): addresses an ISP hands out behind carrier-grade NAT.</summary>
    CarrierGradeNat,

    /// <summary>Loopback, link-local, unspecified and other addresses that never leave a link.</summary>
    Local,
}

/// <summary>Classifies addresses, e.g. to tell a real public address from one behind carrier-grade NAT.</summary>
public static class AddressScope
{
    public static AddressScopeKind Classify(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return AddressScopeKind.Local;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast)
            {
                return AddressScopeKind.Local;
            }

            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC ? AddressScopeKind.Private : AddressScopeKind.Public;
        }

        var b = address.GetAddressBytes();
        return b switch
        {
            [10, ..] => AddressScopeKind.Private,
            [172, >= 16 and <= 31, ..] => AddressScopeKind.Private,
            [192, 168, ..] => AddressScopeKind.Private,
            [100, >= 64 and <= 127, ..] => AddressScopeKind.CarrierGradeNat,
            [169, 254, ..] => AddressScopeKind.Local,
            [>= 224, ..] => AddressScopeKind.Local,
            _ => AddressScopeKind.Public,
        };
    }

    public static bool IsPublic(IPAddress address) => Classify(address) == AddressScopeKind.Public;
}
