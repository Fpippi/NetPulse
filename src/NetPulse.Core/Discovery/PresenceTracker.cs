using NetPulse.Core.Models;
using NetPulse.Core.Outages;

namespace NetPulse.Core.Discovery;

/// <summary>
/// Turns the pings of watched devices into absences: N missed pings in a row start one, the next
/// reply ends it. The same rule as outages of monitored targets, keyed by device instead of target.
/// </summary>
public sealed class PresenceTracker(int missesToDrop)
{
    private readonly OutageTracker _tracker = new(Math.Max(1, missesToDrop));
    private readonly Dictionary<string, string> _ips = new(StringComparer.OrdinalIgnoreCase);

    public int MissesToDrop => _tracker.ConsecutiveLosses;

    /// <summary>Records one ping per device; returns the absences that started, grew or ended.</summary>
    public IReadOnlyList<DeviceAbsence> Observe(IEnumerable<(string Key, string Ip, PingSample Sample)> pings)
    {
        var samples = new List<PingSample>();
        foreach (var (key, ip, sample) in pings)
        {
            _ips[key] = ip;
            samples.Add(sample with { Target = key });
        }

        return _tracker.Observe(samples).Select(ToAbsence).ToList();
    }

    /// <summary>Stops tracking devices no longer watched; their open absences are returned closed.</summary>
    public IReadOnlyList<DeviceAbsence> Retain(IEnumerable<string> watchedKeys)
    {
        var closed = _tracker.Retain(watchedKeys).Select(ToAbsence).ToList();
        foreach (var key in _ips.Keys.Except(watchedKeys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            _ips.Remove(key);
        }

        return closed;
    }

    private DeviceAbsence ToAbsence(Outage o) =>
        new(o.Target, _ips.GetValueOrDefault(o.Target, string.Empty), o.Start, o.LastLost, o.End, o.LostSamples);
}
