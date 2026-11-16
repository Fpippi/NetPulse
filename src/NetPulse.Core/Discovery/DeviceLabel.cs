namespace NetPulse.Core.Discovery;

/// <summary>The name a user can give a device from the dashboard.</summary>
public static class DeviceLabel
{
    public const int MaxLength = 60;

    /// <summary>
    /// Trims the label, collapses inner whitespace and caps the length. An empty or whitespace-only
    /// label becomes null, which clears the device's custom name.
    /// </summary>
    public static string? Normalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var collapsed = string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength].TrimEnd();
    }
}
