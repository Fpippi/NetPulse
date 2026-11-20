using System.Net;
using NetPulse.Core.Traceroute;

namespace NetPulse.Core.PublicIp;

/// <summary>
/// Spots carrier-grade NAT (CGNAT) on the route to the Internet: the ISP shares one public address
/// among many customers, so port forwarding, hosting a server or reaching home through a VPN without a
/// relay cannot work. The tell-tale sign is a hop in 100.64.0.0/10 right after the home router.
/// </summary>
public static class CgnatCheck
{
    /// <summary>Only the first hops count: the ISP's NAT sits right after the home router.</summary>
    public const int HopsToInspect = 4;

    /// <summary>The hop with a carrier-grade NAT address, null when the route shows none.</summary>
    public static TraceHop? FindCgnatHop(TraceResult trace) => trace.Hops
        .Where(h => h.Number <= HopsToInspect)
        .FirstOrDefault(h => h.Address is not null
            && IPAddress.TryParse(h.Address, out var ip)
            && AddressScope.Classify(ip) == AddressScopeKind.CarrierGradeNat);
}

/// <summary>Outcome of the last <see cref="CgnatCheck"/>.</summary>
/// <param name="CheckedAt">When the route was traced.</param>
/// <param name="CgnatHop">The hop with a 100.64/10 address, null when none was seen.</param>
/// <param name="Answered">Hops that answered among the first <see cref="CgnatCheck.HopsToInspect"/>; 0 means the check said nothing.</param>
public sealed record CgnatStatus(DateTimeOffset CheckedAt, TraceHop? CgnatHop, int Answered)
{
    public bool BehindCgnat => CgnatHop is not null;

    public static CgnatStatus From(TraceResult trace) => new(
        trace.Timestamp,
        CgnatCheck.FindCgnatHop(trace),
        trace.Hops.Count(h => h.Number <= CgnatCheck.HopsToInspect && h.Address is not null));
}
