using System.Buffers.Binary;
using System.Text;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Builds a NetBIOS "node status" (NBSTAT) request and reads the reply, which Windows and Samba hosts
/// answer on UDP port 137 with their computer name even when they have no DNS entry.
/// Pure functions so the message handling can be tested without a network.
/// </summary>
public static class NetBiosName
{
    private const int NbstatType = 0x0021;
    private const int InClass = 0x0001;

    // The wildcard NetBIOS name "*", used to ask a host for its own names.
    private static readonly byte[] WildcardName = BuildWildcard();

    public static byte[] BuildNodeStatusRequest(ushort transactionId)
    {
        var message = new byte[12 + WildcardName.Length + 1 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(0), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4), 1); // one question

        WildcardName.CopyTo(message.AsSpan(12));
        var offset = 12 + WildcardName.Length;
        message[offset] = 0; // end of name
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(offset + 1), NbstatType);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(offset + 3), InClass);
        return message;
    }

    /// <summary>
    /// Returns the host's unique workstation name (suffix 0x00, not a group name) from a node status
    /// reply, or null if there is none. Reserved names such as <c>__MSBROWSE__</c> are ignored.
    /// </summary>
    public static string? TryParseNodeStatusResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length < 12)
        {
            return null;
        }

        var answers = BinaryPrimitives.ReadUInt16BigEndian(response[6..]);
        if (answers < 1)
        {
            return null;
        }

        var offset = 12;
        if (!SkipName(response, ref offset) || offset + 11 > response.Length)
        {
            return null;
        }

        // TYPE (2) + CLASS (2) + TTL (4) + RDLENGTH (2), then the name list.
        offset += 10;
        var numberOfNames = response[offset];
        offset += 1;

        for (var i = 0; i < numberOfNames; i++)
        {
            if (offset + 18 > response.Length)
            {
                return null;
            }

            var rawName = response.Slice(offset, 15);
            var suffix = response[offset + 15];
            var flags = BinaryPrimitives.ReadUInt16BigEndian(response[(offset + 16)..]);
            var isGroup = (flags & 0x8000) != 0;
            offset += 18;

            if (suffix == 0x00 && !isGroup)
            {
                var name = Encoding.ASCII.GetString(rawName).TrimEnd(' ', '\0').Trim();
                if (name.Length > 0 && name[0] != '\u0001' && !name.StartsWith("__", StringComparison.Ordinal))
                {
                    return name;
                }
            }
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
                offset += 2;
                return true;
            }

            offset += 1 + length;
        }

        return false;
    }

    private static byte[] BuildWildcard()
    {
        // NetBIOS name: '*' then 15 zero bytes, first-level encoded (each nibble + 'A').
        Span<byte> name = stackalloc byte[16];
        name.Clear();
        name[0] = (byte)'*';

        var encoded = new byte[1 + 32];
        encoded[0] = 32; // length of the encoded name
        for (var i = 0; i < 16; i++)
        {
            encoded[1 + (i * 2)] = (byte)('A' + (name[i] >> 4));
            encoded[1 + (i * 2) + 1] = (byte)('A' + (name[i] & 0x0F));
        }

        return encoded;
    }
}
