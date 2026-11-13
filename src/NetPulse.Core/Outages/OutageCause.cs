namespace NetPulse.Core.Outages;

/// <summary>Where an outage most likely came from, judged by what else was down at the same time.</summary>
public enum OutageCause
{
    /// <summary>Not enough information (e.g. the gateway is not monitored).</summary>
    Unknown,

    /// <summary>The router itself did not answer: Wi-Fi, cable, router or this machine.</summary>
    LocalNetwork,

    /// <summary>The router answered but every public host was down: the Internet connection (ISP).</summary>
    Internet,

    /// <summary>Only this host was down while others answered: a problem of that host, not of the connection.</summary>
    SingleHost,
}

/// <summary>An outage with its likely cause.</summary>
public sealed record ClassifiedOutage(Outage Outage, OutageCause Cause);
