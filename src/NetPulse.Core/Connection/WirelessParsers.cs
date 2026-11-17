using System.Globalization;
using System.Text.RegularExpressions;

namespace NetPulse.Core.Connection;

/// <summary>What a platform tool says about the current Wi-Fi connection.</summary>
public sealed record WifiReading(string? Interface, string? Ssid, int? SignalDbm, double? RateMbps);

/// <summary>
/// Parsers for the text that Linux and Windows expose about Wi-Fi. Pure functions, so every platform's
/// output is covered by tests on any machine.
/// </summary>
public static partial class WirelessParsers
{
    /// <summary>
    /// Linux <c>/proc/net/wireless</c>: the signal level (dBm) of each wireless interface.
    /// <code>
    ///  face | tus | link level noise | ...
    ///  wlan0: 0000   54.  -56.  -256  ...
    /// </code>
    /// </summary>
    public static IReadOnlyDictionary<string, int> ParseProcNetWireless(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // status, link quality, level, noise, ...
            if (name.Length == 0 || name.Contains('|') || fields.Length < 3)
            {
                continue;
            }

            if (double.TryParse(fields[2].TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var level))
            {
                // Old drivers report an unsigned byte (e.g. 200 for −56 dBm).
                var dbm = (int)Math.Round(level > 0 ? level - 256 : level);
                if (dbm is < 0 and > -120)
                {
                    result[name] = dbm;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Linux <c>iw dev &lt;if&gt; link</c>: SSID, signal and transmit rate; null when not connected.
    /// <code>
    /// Connected to aa:bb:cc:dd:ee:ff (on wlan0)
    ///     SSID: HomeNet
    ///     signal: -56 dBm
    ///     tx bitrate: 866.7 MBit/s VHT-MCS 9 80MHz short GI VHT-NSS 2
    /// </code>
    /// </summary>
    public static WifiReading? ParseIwLink(string text)
    {
        if (!text.TrimStart().StartsWith("Connected", StringComparison.Ordinal))
        {
            return null;
        }

        string? ssid = null;
        int? signal = null;
        double? rate = null;
        var iface = IwInterface().Match(text) is { Success: true } m ? m.Groups[1].Value : null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("SSID:", StringComparison.Ordinal))
            {
                ssid = line["SSID:".Length..].Trim();
            }
            else if (line.StartsWith("signal:", StringComparison.Ordinal) && LeadingNumber(line["signal:".Length..]) is { } dbm)
            {
                signal = (int)Math.Round(dbm);
            }
            else if (line.StartsWith("tx bitrate:", StringComparison.Ordinal))
            {
                rate = LeadingNumber(line["tx bitrate:".Length..]);
            }
        }

        return new WifiReading(iface, string.IsNullOrEmpty(ssid) ? null : ssid, signal, rate);
    }

    /// <summary>
    /// Windows <c>netsh wlan show interfaces</c>, in any display language: the labels are translated
    /// ("Signal", "Segnale", …) but the layout is not, so the parser relies on it. Per interface block it
    /// takes the first value as the name, <c>SSID</c> as the network (only present when connected), the
    /// first percentage as the signal and the first "rate"-like number in Mbps as the speed.
    /// Returns the first connected interface, or null.
    /// </summary>
    public static WifiReading? ParseNetshWlan(string text)
    {
        foreach (var block in NetshBlocks(text))
        {
            string? name = null;
            string? ssid = null;
            int? signal = null;
            double? rate = null;

            foreach (var (key, value) in block)
            {
                name ??= value;
                if (key == "SSID")
                {
                    ssid = value;
                }
                else if (signal is null && NetshPercent().Match(value) is { Success: true } percent)
                {
                    signal = WifiSignal.PercentToDbm(int.Parse(percent.Groups[1].Value, CultureInfo.InvariantCulture));
                }
                else if (rate is null && key.Contains("(Mbps)", StringComparison.OrdinalIgnoreCase) && LeadingNumber(value) is { } mbps)
                {
                    rate = mbps;
                }
            }

            if (!string.IsNullOrEmpty(ssid))
            {
                return new WifiReading(name, ssid, signal, rate);
            }
        }

        return null;
    }

    private static IEnumerable<List<(string Key, string Value)>> NetshBlocks(string text)
    {
        var block = new List<(string, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var colon = line.IndexOf(" : ", StringComparison.Ordinal);
            if (colon < 0)
            {
                // A blank line between interfaces ends a block (headers have no " : ").
                if (line.Trim().Length == 0 && block.Count > 0)
                {
                    yield return block;
                    block = [];
                }

                continue;
            }

            block.Add((line[..colon].Trim(), line[(colon + 3)..].Trim()));
        }

        if (block.Count > 0)
        {
            yield return block;
        }
    }

    private static double? LeadingNumber(string text) =>
        LeadingNumberRegex().Match(text) is { Success: true } m
        && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    [GeneratedRegex(@"\(on ([^)\s]+)\)")]
    private static partial Regex IwInterface();

    [GeneratedRegex(@"^(\d{1,3})\s?%$")]
    private static partial Regex NetshPercent();

    [GeneratedRegex(@"^\s*-?\d+(\.\d+)?")]
    private static partial Regex LeadingNumberRegex();
}
