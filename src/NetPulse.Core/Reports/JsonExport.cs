using System.Text.Encodings.Web;
using System.Text.Json;
using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Core.Reports;

/// <summary>
/// Writes NetPulse data as a JSON array of flat objects, one per record, oldest first. Field names and
/// values match <see cref="CsvExport"/> (same columns, rounding and UTC timestamps), with real numbers,
/// booleans and nulls, so the file loads straight into jq, pandas, Excel's Power Query or a script.
/// Records are streamed: long periods never sit in memory.
/// </summary>
public static class JsonExport
{
    /// <summary>Flush to the output every this many records.</summary>
    private const int FlushEvery = 500;

    private static readonly JsonWriterOptions Options = new()
    {
        // Target names and error messages are data, not HTML: keep them readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteSamplesAsync(
        Stream stream, IAsyncEnumerable<PingSample> samples, CancellationToken cancellationToken = default)
    {
        await using var json = new Utf8JsonWriter(stream, Options);
        json.WriteStartArray();
        var count = 0;
        await foreach (var s in samples.WithCancellation(cancellationToken))
        {
            json.WriteStartObject();
            json.WriteString("timestamp_utc", Csv.Timestamp(s.Timestamp));
            json.WriteString("target", s.Target);
            json.WriteBoolean("success", s.Success);
            Number(json, "latency_ms", s.LatencyMs);
            json.WriteEndObject();

            if (++count % FlushEvery == 0)
            {
                await json.FlushAsync(cancellationToken);
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
    }

    /// <param name="now">End used for the duration of ongoing outages.</param>
    public static async Task WriteOutagesAsync(
        Stream stream, IEnumerable<ClassifiedOutage> outages, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var json = new Utf8JsonWriter(stream, Options);
        json.WriteStartArray();
        foreach (var (o, cause) in outages)
        {
            json.WriteStartObject();
            json.WriteString("start_utc", Csv.Timestamp(o.Start));
            Timestamp(json, "end_utc", o.End);
            json.WriteString("target", o.Target);
            Number(json, "duration_seconds", o.Duration(now).TotalSeconds, 1);
            json.WriteNumber("lost_pings", o.LostSamples);
            json.WriteBoolean("ongoing", o.IsOngoing);
            json.WriteString("cause", cause.ToString());
            json.WriteEndObject();
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
    }

    public static async Task WriteSpeedTestsAsync(
        Stream stream, IEnumerable<SpeedTestResult> results, CancellationToken cancellationToken = default)
    {
        await using var json = new Utf8JsonWriter(stream, Options);
        json.WriteStartArray();
        foreach (var r in results)
        {
            json.WriteStartObject();
            json.WriteString("timestamp_utc", Csv.Timestamp(r.Timestamp));
            json.WriteString("server", r.Server);
            Number(json, "download_mbps", r.DownloadMbps);
            Number(json, "upload_mbps", r.UploadMbps);
            Number(json, "latency_ms", r.LatencyMs);
            json.WriteNumber("download_bytes", r.DownloadBytes);
            json.WriteNumber("upload_bytes", r.UploadBytes);
            if (r.Error is null)
            {
                json.WriteNull("error");
            }
            else
            {
                json.WriteString("error", r.Error);
            }

            Number(json, "idle_ping_ms", r.Bufferbloat?.IdleMs);
            Number(json, "download_ping_ms", r.Bufferbloat?.DownloadMs);
            Number(json, "upload_ping_ms", r.Bufferbloat?.UploadMs);
            if (r.Bufferbloat?.Grade is { } grade)
            {
                json.WriteString("bufferbloat_grade", Bufferbloat.Label(grade));
            }
            else
            {
                json.WriteNull("bufferbloat_grade");
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
    }

    private static void Number(Utf8JsonWriter json, string name, double? value, int decimals = 2)
    {
        if (value is { } v && double.IsFinite(v))
        {
            json.WriteNumber(name, Math.Round(v, decimals));
        }
        else
        {
            json.WriteNull(name);
        }
    }

    private static void Timestamp(Utf8JsonWriter json, string name, DateTimeOffset? value)
    {
        if (value is { } v)
        {
            json.WriteString(name, Csv.Timestamp(v));
        }
        else
        {
            json.WriteNull(name);
        }
    }
}
