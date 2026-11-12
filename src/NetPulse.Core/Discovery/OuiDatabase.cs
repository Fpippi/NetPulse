namespace NetPulse.Core.Discovery;

/// <summary>
/// Maps the first bits of a MAC address (the OUI) to the manufacturer that registered it.
/// The data file has one assignment per line, <c>PREFIX&lt;TAB&gt;Vendor</c>, where the prefix is
/// 6 (MA-L), 7 (MA-M) or 9 (MA-S) hex digits. Lines starting with <c>#</c> are comments.
/// </summary>
public sealed class OuiDatabase
{
    private static readonly int[] PrefixLengths = [9, 7, 6];

    private readonly Dictionary<string, string> _vendors;

    private OuiDatabase(Dictionary<string, string> vendors) => _vendors = vendors;

    /// <summary>A database without entries: every lookup returns null.</summary>
    public static OuiDatabase Empty { get; } = new([]);

    public int Count => _vendors.Count;

    public static OuiDatabase Load(TextReader reader)
    {
        var vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            if (tab is not (6 or 7 or 9))
            {
                continue;
            }

            var vendor = line[(tab + 1)..].Trim();
            if (vendor.Length > 0)
            {
                vendors.TryAdd(line[..tab], vendor);
            }
        }

        return new OuiDatabase(vendors);
    }

    public static OuiDatabase LoadFile(string path)
    {
        using var reader = new StreamReader(path);
        return Load(reader);
    }

    /// <summary>
    /// Returns the vendor of a MAC address, or null when it is unknown or randomized
    /// (locally administered addresses are not registered with the IEEE).
    /// </summary>
    public string? Lookup(string? mac)
    {
        if (!MacAddress.TryNormalize(mac, out var canonical) || MacAddress.IsLocallyAdministered(canonical))
        {
            return null;
        }

        var hex = canonical.Replace(":", string.Empty);

        // Longest match first: a large MA-L block can be split into smaller MA-M / MA-S assignments.
        foreach (var length in PrefixLengths)
        {
            if (_vendors.TryGetValue(hex[..length], out var vendor))
            {
                return vendor;
            }
        }

        return null;
    }
}
