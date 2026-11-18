namespace NetPulse.Core.Discovery;

/// <summary>A device on the watch list.</summary>
/// <param name="Key">The device's key (see <see cref="LanDevice.Key"/>).</param>
/// <param name="Since">When watching started.</param>
public sealed record WatchedDevice(string Key, DateTimeOffset Since);

/// <summary>
/// A stretch of time in which a watched device stopped answering pings: it dropped off the Wi-Fi,
/// was switched off or went out of range.
/// </summary>
/// <param name="DeviceKey">The device's key.</param>
/// <param name="Ip">Address that was pinged.</param>
/// <param name="Start">First missed ping.</param>
/// <param name="LastMissed">Latest missed ping.</param>
/// <param name="End">First reply after the absence; null while the device is still away.</param>
/// <param name="MissedPings">Pings without a reply.</param>
public sealed record DeviceAbsence(
    string DeviceKey,
    string Ip,
    DateTimeOffset Start,
    DateTimeOffset LastMissed,
    DateTimeOffset? End,
    int MissedPings)
{
    public bool IsOngoing => End is null;

    public TimeSpan Duration(DateTimeOffset now)
    {
        var duration = (End ?? now) - Start;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }
}
