namespace NetPulse.Core.Discovery;

/// <summary>Result of merging a scan into the known devices.</summary>
/// <param name="Devices">Every known device, updated.</param>
/// <param name="Events">What changed: new devices, devices that went offline or came back.</param>
public sealed record DeviceScanUpdate(IReadOnlyList<LanDevice> Devices, IReadOnlyList<DeviceEvent> Events);

/// <summary>
/// Keeps the device inventory up to date from successive scans. Pure logic: no I/O and no clock.
/// </summary>
/// <param name="offlineAfterMissedScans">
/// How many scans in a row a device may miss before it is reported offline. More than one avoids
/// false alarms from phones that sleep or packets that get lost.
/// </param>
/// <param name="vendorLookup">Resolves a MAC address to a vendor name.</param>
public sealed class DeviceTracker(int offlineAfterMissedScans, Func<string?, string?> vendorLookup)
{
    private readonly int _offlineAfter = Math.Max(1, offlineAfterMissedScans);

    /// <summary>
    /// Merges <paramref name="observed"/> into <paramref name="known"/>.
    /// The very first scan (no known devices) builds the baseline and reports no "new device" events,
    /// otherwise every device on the network would show up as an alert at first start.
    /// </summary>
    public DeviceScanUpdate Apply(
        IReadOnlyList<LanDevice> known,
        IReadOnlyList<DeviceObservation> observed,
        DateTimeOffset now)
    {
        var isBaseline = known.Count == 0;
        var devices = known.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        var events = new List<DeviceEvent>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var observation in observed.DistinctBy(o => LanDevice.KeyFor(o.Mac, o.Ip), StringComparer.OrdinalIgnoreCase))
        {
            var key = LanDevice.KeyFor(observation.Mac, observation.Ip);
            seen.Add(key);

            if (devices.TryGetValue(key, out var device))
            {
                var updated = device with
                {
                    Ip = observation.Ip,
                    Hostname = observation.Hostname ?? (device.Ip == observation.Ip ? device.Hostname : null),
                    Vendor = device.Vendor ?? vendorLookup(observation.Mac),
                    LastSeen = now,
                    IsOnline = true,
                    MissedScans = 0,
                };

                if (!device.IsOnline)
                {
                    events.Add(Event(now, DeviceEventKind.Online, updated));
                }

                devices[key] = updated;
            }
            else
            {
                var added = new LanDevice(
                    key,
                    observation.Ip,
                    observation.Mac,
                    vendorLookup(observation.Mac),
                    observation.Hostname,
                    FirstSeen: now,
                    LastSeen: now,
                    IsOnline: true,
                    MissedScans: 0);

                if (!isBaseline)
                {
                    events.Add(Event(now, DeviceEventKind.New, added));
                }

                devices[key] = added;
            }
        }

        foreach (var device in devices.Values.Where(d => !seen.Contains(d.Key)).ToList())
        {
            var missed = device.MissedScans + 1;
            var goesOffline = device.IsOnline && missed >= _offlineAfter;
            var updated = device with { MissedScans = missed, IsOnline = device.IsOnline && !goesOffline };

            if (goesOffline)
            {
                events.Add(Event(now, DeviceEventKind.Offline, updated));
            }

            devices[device.Key] = updated;
        }

        return new DeviceScanUpdate(Sort(devices.Values), events);
    }

    /// <summary>Online devices first, then by IP address in numeric order.</summary>
    public static IReadOnlyList<LanDevice> Sort(IEnumerable<LanDevice> devices) =>
        devices
            .OrderByDescending(d => d.IsOnline)
            .ThenBy(d => IpSortKey(d.Ip))
            .ThenBy(d => d.Key, StringComparer.Ordinal)
            .ToList();

    private static DeviceEvent Event(DateTimeOffset now, DeviceEventKind kind, LanDevice device) =>
        new(now, kind, device.Key, device.Ip, device.DisplayName);

    private static long IpSortKey(string ip) =>
        System.Net.IPAddress.TryParse(ip, out var address) && address.GetAddressBytes() is { Length: 4 } b
            ? ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3]
            : long.MaxValue;
}
