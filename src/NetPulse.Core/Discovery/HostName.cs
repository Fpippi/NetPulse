using System.Net;

namespace NetPulse.Core.Discovery;

/// <summary>Tidies up the raw names returned by the resolvers.</summary>
public static class HostName
{
    /// <summary>
    /// Trims the name, drops a trailing dot and an empty or numeric result (some resolvers echo the
    /// address when there is no record). Returns null when nothing useful is left.
    /// </summary>
    public static string? Clean(string? name)
    {
        var trimmed = name?.Trim().TrimEnd('.').Trim();
        return string.IsNullOrEmpty(trimmed) || IPAddress.TryParse(trimmed, out _) ? null : trimmed;
    }
}
