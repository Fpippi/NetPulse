using System.Globalization;
using System.Text;

namespace NetPulse.Core.Metrics;

/// <summary>
/// Writes the Prometheus text exposition format (version 0.0.4) by hand: a few gauges do not justify a
/// client library. Each metric family gets its HELP and TYPE lines once, followed by its samples.
/// </summary>
public sealed class PrometheusWriter
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    private readonly StringBuilder _text = new();
    private readonly HashSet<string> _declared = [];

    /// <summary>Adds a gauge sample; null values are skipped (Prometheus has no "unknown").</summary>
    public PrometheusWriter Gauge(string name, string help, double? value, params (string Name, string Value)[] labels)
    {
        if (value is not { } v || double.IsNaN(v))
        {
            return this;
        }

        if (_declared.Add(name))
        {
            _text.Append("# HELP ").Append(name).Append(' ').Append(EscapeHelp(help)).Append('\n');
            _text.Append("# TYPE ").Append(name).Append(" gauge\n");
        }

        _text.Append(name);
        if (labels.Length > 0)
        {
            _text.Append('{')
                .AppendJoin(',', labels.Select(l => $"{l.Name}=\"{EscapeLabel(l.Value)}\""))
                .Append('}');
        }

        _text.Append(' ').Append(Format(v)).Append('\n');
        return this;
    }

    public override string ToString() => _text.ToString();

    /// <summary>Numbers with a dot and no thousands separator, whatever the machine's culture.</summary>
    public static string Format(double value) => value switch
    {
        double.PositiveInfinity => "+Inf",
        double.NegativeInfinity => "-Inf",
        _ => value.ToString("R", CultureInfo.InvariantCulture),
    };

    /// <summary>Label values escape backslash, double quote and line feed.</summary>
    public static string EscapeLabel(string value) =>
        value.Replace("\\", @"\\").Replace("\"", "\\\"").Replace("\n", @"\n");

    private static string EscapeHelp(string value) => value.Replace("\\", @"\\").Replace("\n", @"\n");
}
