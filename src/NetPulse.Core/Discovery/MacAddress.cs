using System.Globalization;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Helpers for MAC addresses written in the usual notations
/// (<c>aa:bb:cc:dd:ee:ff</c>, <c>AA-BB-CC-DD-EE-FF</c>, <c>aabb.ccdd.eeff</c>, macOS <c>0:1a:2b:3:4:5</c>).
/// The canonical form is upper case with colons: <c>AA:BB:CC:DD:EE:FF</c>.
/// </summary>
public static class MacAddress
{
    /// <summary>Parses a MAC address into its canonical form. Returns false for anything else.</summary>
    public static bool TryNormalize(string? input, out string mac)
    {
        mac = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        string[] octets;

        if (text.Contains(':') || text.Contains('-'))
        {
            // Six groups of one or two hex digits (macOS drops leading zeros).
            octets = text.Split(':', '-');
            if (octets.Length != 6 || octets.Any(o => o.Length is < 1 or > 2))
            {
                return false;
            }
        }
        else
        {
            // Cisco style (aabb.ccdd.eeff) or bare hex (aabbccddeeff).
            var hex = text.Replace(".", string.Empty);
            if (hex.Length != 12)
            {
                return false;
            }

            octets = Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)).ToArray();
        }

        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            if (!byte.TryParse(octets[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return false;
            }
        }

        mac = string.Join(':', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        return true;
    }

    /// <summary>
    /// True for addresses that identify a single real device: not all zeros (incomplete ARP entry),
    /// not broadcast and not multicast.
    /// </summary>
    public static bool IsUnicastDevice(string canonicalMac) =>
        canonicalMac != "00:00:00:00:00:00" && (FirstOctet(canonicalMac) & 0x01) == 0;

    /// <summary>
    /// True when the "locally administered" bit is set. Phones and laptops use such random addresses
    /// for privacy, so they have no vendor.
    /// </summary>
    public static bool IsLocallyAdministered(string canonicalMac) => (FirstOctet(canonicalMac) & 0x02) != 0;

    private static int FirstOctet(string canonicalMac) =>
        int.Parse(canonicalMac.AsSpan(0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
}
