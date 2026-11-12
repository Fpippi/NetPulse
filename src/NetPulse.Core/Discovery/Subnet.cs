using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace NetPulse.Core.Discovery;

/// <summary>An IPv4 network in CIDR notation, e.g. <c>192.168.1.0/24</c>.</summary>
public sealed record Subnet
{
    private Subnet(uint network, int prefixLength)
    {
        NetworkValue = network;
        PrefixLength = prefixLength;
    }

    private uint NetworkValue { get; }

    public int PrefixLength { get; }

    public IPAddress Network => ToAddress(NetworkValue);

    /// <summary>Number of addresses that can be assigned to hosts.</summary>
    public long HostCount => PrefixLength switch
    {
        32 => 1,
        31 => 2,
        _ => (1L << (32 - PrefixLength)) - 2,
    };

    public static Subnet Create(IPAddress address, int prefixLength)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Only IPv4 is supported.", nameof(address));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, 32);

        return new Subnet(ToUInt(address) & Mask(prefixLength), prefixLength);
    }

    /// <summary>Builds the subnet from an interface address and its netmask (e.g. 255.255.255.0).</summary>
    public static Subnet FromNetmask(IPAddress address, IPAddress netmask)
    {
        var mask = ToUInt(netmask);
        var prefix = System.Numerics.BitOperations.PopCount(mask);
        if (mask != Mask(prefix))
        {
            throw new ArgumentException($"{netmask} is not a valid netmask.", nameof(netmask));
        }

        return Create(address, prefix);
    }

    /// <summary>Parses <c>a.b.c.d/n</c>. The host bits of the address are cleared.</summary>
    public static bool TryParse(string? text, out Subnet? subnet)
    {
        subnet = null;
        var parts = text?.Trim().Split('/');
        if (parts is not { Length: 2 }
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix > 32)
        {
            return false;
        }

        subnet = Create(address, prefix);
        return true;
    }

    public bool Contains(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork
        && (ToUInt(address) & Mask(PrefixLength)) == NetworkValue;

    /// <summary>
    /// Returns this subnet if it is at most as large as <paramref name="maxSizePrefix"/>, otherwise the
    /// smaller subnet of that size containing <paramref name="around"/>. Keeps a sweep of a /16 from
    /// sending 65,000 pings.
    /// </summary>
    public Subnet LimitTo(int maxSizePrefix, IPAddress around) =>
        PrefixLength >= maxSizePrefix || !Contains(around) ? this : Create(around, maxSizePrefix);

    /// <summary>All host addresses, without the network and broadcast addresses.</summary>
    public IEnumerable<IPAddress> Hosts()
    {
        if (PrefixLength >= 31)
        {
            for (var i = 0L; i < HostCount; i++)
            {
                yield return ToAddress(NetworkValue + (uint)i);
            }

            yield break;
        }

        var broadcast = NetworkValue | ~Mask(PrefixLength);
        for (var value = NetworkValue + 1; value < broadcast; value++)
        {
            yield return ToAddress(value);
        }
    }

    public override string ToString() => $"{Network}/{PrefixLength.ToString(CultureInfo.InvariantCulture)}";

    private static uint Mask(int prefixLength) => prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);

    private static uint ToUInt(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress ToAddress(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}
