using System.Globalization;
using NetPulse.Core.Stats;

namespace NetPulse.Core.Connection;

/// <summary>Wi-Fi signal strength helpers.</summary>
public static class WifiSignal
{
    /// <summary>At or above this, video calls and gaming are fine.</summary>
    public const int GoodDbm = -60;

    /// <summary>Below this, expect lost packets, retries and latency spikes.</summary>
    public const int PoorDbm = -70;

    public static ConnectionQuality Rate(int? dbm) => dbm switch
    {
        null => ConnectionQuality.Unknown,
        >= GoodDbm => ConnectionQuality.Good,
        >= PoorDbm => ConnectionQuality.Fair,
        _ => ConnectionQuality.Poor,
    };

    /// <summary>
    /// Windows reports the signal as a percentage, which it derives linearly from RSSI:
    /// 0% ≈ −100 dBm, 100% ≈ −50 dBm.
    /// </summary>
    public static int PercentToDbm(int percent) => Math.Clamp(percent, 0, 100) / 2 - 100;

    /// <summary>"−58 dBm" with a real minus sign, invariant digits.</summary>
    public static string Format(int dbm) =>
        (dbm < 0 ? "−" : string.Empty) + Math.Abs(dbm).ToString(CultureInfo.InvariantCulture) + " dBm";
}
