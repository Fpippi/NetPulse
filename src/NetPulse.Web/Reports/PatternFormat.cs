using System.Globalization;

namespace NetPulse.Web.Reports;

/// <summary>Number formats of the Patterns page, invariant so that it-IT machines show '.' too.</summary>
public static class PatternFormat
{
    public static string Ms(double? value) =>
        value is null ? "–" : value.Value.ToString(value < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " ms";

    public static string Mbps(double? value) =>
        value is null ? "–" : value.Value.ToString(value < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " Mbps";

    public static string Percent(double? value) =>
        value is null ? "–" : value.Value.ToString(value < 1 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " %";
}
