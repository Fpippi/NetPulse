using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Minimal DNS wire-format helpers, enough to ask for and read a reverse (PTR) record.
/// Shared by unicast DNS-over-UDP and multicast DNS (mDNS), which use the same message format.
/// Pure functions so the parsing can be tested without a network.
/// </summary>
public static class DnsWire
{
    private const int PtrType = 12;
    private const int InClass = 1;

    /// <summary>Builds a query for the PTR record of <paramref name="address"/> (e.g. 4.3.2.1.in-addr.arpa).</summary>
    public static byte[] BuildReversePtrQuery(IPAddress address, ushort transactionId)
    {
        var octets = address.GetAddressBytes();
        var qname = $"{octets[3]}.{octets[2]}.{octets[1]}.{octets[0]}.in-addr.arpa";

        var message = new List<byte>();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[0..], transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0000); // standard query, no recursion
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // one question
        message.AddRange(header.ToArray());

        foreach (var label in qname.Split('.'))
        {
            message.Add((byte)label.Length);
            message.AddRange(Encoding.ASCII.GetBytes(label));
        }

        message.Add(0); // root label

        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(tail[0..], PtrType);
        BinaryPrimitives.WriteUInt16BigEndian(tail[2..], InClass);
        message.AddRange(tail.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// Returns the host name from the first PTR answer in a response, or null if there is none.
    /// Handles DNS name compression. The trailing dot is removed.
    /// </summary>
    public static string? TryReadFirstPtrTarget(ReadOnlySpan<byte> response)
    {
        if (response.Length < 12)
        {
            return null;
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(response[4..]);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(response[6..]);

        var offset = 12;
        for (var i = 0; i < questions; i++)
        {
            if (!SkipName(response, ref offset) || offset + 4 > response.Length)
            {
                return null;
            }

            offset += 4; // QTYPE + QCLASS
        }

        for (var i = 0; i < answers; i++)
        {
            if (!SkipName(response, ref offset) || offset + 10 > response.Length)
            {
                return null;
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(response[offset..]);
            var rdLength = BinaryPrimitives.ReadUInt16BigEndian(response[(offset + 8)..]);
            var rdOffset = offset + 10;

            if (rdOffset + rdLength > response.Length)
            {
                return null;
            }

            if (type == PtrType && TryReadName(response, rdOffset, out var name))
            {
                return name;
            }

            offset = rdOffset + rdLength;
        }

        return null;
    }

    private static bool SkipName(ReadOnlySpan<byte> data, ref int offset)
    {
        while (offset < data.Length)
        {
            var length = data[offset];
            if (length == 0)
            {
                offset++;
                return true;
            }

            if ((length & 0xC0) == 0xC0)
            {
                offset += 2; // compression pointer ends the name
                return true;
            }

            offset += 1 + length;
        }

        return false;
    }

    private static bool TryReadName(ReadOnlySpan<byte> data, int offset, out string name)
    {
        var labels = new List<string>();
        var jumps = 0;

        while (offset >= 0 && offset < data.Length)
        {
            var length = data[offset];
            if (length == 0)
            {
                name = string.Join('.', labels);
                return name.Length > 0;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (offset + 1 >= data.Length || ++jumps > 16)
                {
                    break;
                }

                offset = ((length & 0x3F) << 8) | data[offset + 1];
                continue;
            }

            if (offset + 1 + length > data.Length)
            {
                break;
            }

            labels.Add(Encoding.ASCII.GetString(data.Slice(offset + 1, length)));
            offset += 1 + length;
        }

        name = string.Empty;
        return false;
    }
}
