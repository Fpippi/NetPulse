using NetPulse.Core.Models;
using NetPulse.Core.Probing;

namespace NetPulse.Core.ClientCheck;

/// <summary>Whether the NetPulse machine itself reaches the Internet right now.</summary>
public enum HostInternetState
{
    /// <summary>No recent samples of public targets.</summary>
    Unknown,

    Up,

    /// <summary>Every recent probe of every public target failed.</summary>
    Down,
}

public static class HostInternet
{
    /// <summary>
    /// Reads the state from the monitor's recent samples: up if any public target (anything but the
    /// gateways) answered, down if all of them failed.
    /// </summary>
    public static HostInternetState From(IEnumerable<PingSample> recent)
    {
        var known = false;
        foreach (var sample in recent)
        {
            if (string.Equals(sample.Target, ProbeTarget.Gateway4, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sample.Target, ProbeTarget.Gateway6, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sample.Success)
            {
                return HostInternetState.Up;
            }

            known = true;
        }

        return known ? HostInternetState.Down : HostInternetState.Unknown;
    }
}
