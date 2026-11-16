namespace NetPulse.Core.Discovery;

/// <summary>A device seen on the local network.</summary>
/// <param name="Key">
/// Stable identity: the MAC address when known (it survives IP changes), otherwise <c>ip:</c> + the address.
/// </param>
/// <param name="Ip">Last IPv4 address the device used.</param>
/// <param name="Mac">MAC address in canonical form, if known.</param>
/// <param name="Vendor">Manufacturer from the OUI database, if known.</param>
/// <param name="Hostname">Name from reverse DNS, if any.</param>
/// <param name="FirstSeen">When the device was first discovered.</param>
/// <param name="LastSeen">When the device last answered.</param>
/// <param name="IsOnline">False once the device missed enough consecutive scans.</param>
/// <param name="MissedScans">Consecutive scans in which the device did not answer.</param>
/// <param name="Label">A name the user gave this device, kept across scans. Wins over the detected name.</param>
public sealed record LanDevice(
    string Key,
    string Ip,
    string? Mac,
    string? Vendor,
    string? Hostname,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    bool IsOnline,
    int MissedScans,
    string? Label = null)
{
    /// <summary>Best human readable name: the user's label, then host name, then vendor, then IP.</summary>
    public string DisplayName => Label ?? Hostname ?? Vendor ?? Ip;

    public static string KeyFor(string? mac, string ip) => mac ?? $"ip:{ip}";
}

/// <summary>What one scan saw of a device.</summary>
/// <param name="Ip">IPv4 address.</param>
/// <param name="Mac">MAC address in canonical form, if the ARP table had it.</param>
/// <param name="Hostname">Name from reverse DNS, if any.</param>
public sealed record DeviceObservation(string Ip, string? Mac, string? Hostname);

public enum DeviceEventKind
{
    /// <summary>A device never seen before joined the network.</summary>
    New,

    /// <summary>A known device stopped answering.</summary>
    Offline,

    /// <summary>A device that was offline is back.</summary>
    Online,
}

/// <summary>A change in the device list, shown on the dashboard as an alert.</summary>
public sealed record DeviceEvent(DateTimeOffset Timestamp, DeviceEventKind Kind, string DeviceKey, string Ip, string Name);
