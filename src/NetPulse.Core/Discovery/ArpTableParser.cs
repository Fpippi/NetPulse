using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NetPulse.Core.Discovery;

/// <summary>An IPv4 to MAC mapping from the operating system's neighbour (ARP) table.</summary>
/// <param name="Ip">IPv4 address.</param>
/// <param name="Mac">MAC address in canonical form (see <see cref="MacAddress"/>).</param>
public sealed record ArpEntry(IPAddress Ip, string Mac);

/// <summary>
/// Parses the ARP table as printed by the operating system. Pure text processing, so it is fully testable.
/// </summary>
public static partial class ArpTableParser
{
    /// <summary>
    /// Parses Linux <c>/proc/net/arp</c>:
    /// <code>
    /// IP address       HW type     Flags       HW address            Mask     Device
    /// 192.168.1.1      0x1         0x2         aa:bb:cc:dd:ee:ff     *        eth0
    /// </code>
    /// Entries with flags 0x0 are incomplete (the host did not answer) and are skipped.
    /// </summary>
    public static IReadOnlyList<ArpEntry> ParseProcNetArp(string content)
    {
        var entries = new List<ArpEntry>();

        foreach (var line in Lines(content).Skip(1))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 4 || columns[2] == "0x0")
            {
                continue;
            }

            Add(entries, columns[0], columns[3]);
        }

        return Distinct(entries);
    }

    /// <summary>
    /// Parses the output of <c>arp -a</c> on Windows
    /// (<c>192.168.1.1   aa-bb-cc-dd-ee-ff   dynamic</c>) and of <c>arp -an</c> on macOS / BSD
    /// (<c>? (192.168.1.1) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]</c>).
    /// Lines without both an IPv4 and a MAC address (headers, <c>(incomplete)</c>) are ignored.
    /// </summary>
    public static IReadOnlyList<ArpEntry> ParseArpCommand(string output)
    {
        var entries = new List<ArpEntry>();

        foreach (var line in Lines(output))
        {
            var ip = Ipv4Regex().Match(line);
            var mac = MacRegex().Match(line);
            if (ip.Success && mac.Success)
            {
                Add(entries, ip.Value, mac.Value);
            }
        }

        return Distinct(entries);
    }

    private static void Add(List<ArpEntry> entries, string ipText, string macText)
    {
        if (IPAddress.TryParse(ipText, out var ip)
            && ip.AddressFamily == AddressFamily.InterNetwork
            && MacAddress.TryNormalize(macText, out var mac)
            && MacAddress.IsUnicastDevice(mac))
        {
            entries.Add(new ArpEntry(ip, mac));
        }
    }

    // The same host can appear once per interface; keep the first occurrence.
    private static List<ArpEntry> Distinct(List<ArpEntry> entries) =>
        entries.DistinctBy(e => e.Ip).ToList();

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{1,2}(?:[:-][0-9A-Fa-f]{1,2}){5}\b")]
    private static partial Regex MacRegex();
}
