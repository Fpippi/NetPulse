namespace NetPulse.Core.Targets;

/// <summary>
/// Helpers to combine the targets from configuration with the ones added by the user.
/// </summary>
public static class TargetList
{
    /// <summary>
    /// Returns configured targets first, then custom ones, without duplicates
    /// (compared case-insensitively) and preserving order.
    /// </summary>
    public static IReadOnlyList<string> Merge(IEnumerable<string> configured, IEnumerable<string> custom) =>
        configured
            .Concat(custom)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
