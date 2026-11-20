using System.Globalization;
using System.Text;
using NetPulse.Core.Reports;
using NetPulse.Core.Stats;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;

namespace NetPulse.Web.Reports;

/// <summary>Patterns, the ISP report and the CSV/JSON exports.</summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder api)
    {
        // Patterns: the last days folded onto one week (weekday x hour, server time).
        api.MapGet("/patterns", async (PatternService patterns, TargetManager targets, int days = 30, string? target = null,
            CancellationToken ct = default) =>
        {
            if (days < 1 || days > PatternService.PeriodDays[^1])
            {
                return Results.BadRequest(new { error = $"days must be between 1 and {PatternService.PeriodDays[^1]}." });
            }

            target ??= PatternService.DefaultTarget(await targets.GetEffectiveTargetsAsync(ct));
            var heatmap = await patterns.BuildAsync(days, target, ct);
            return Results.Ok(new
            {
                days,
                target,
                timeZone = TimeZoneInfo.Local.Id,
                typical = new
                {
                    latencyMs = heatmap.Typical(HeatmapMetric.Latency),
                    lossPercent = heatmap.Typical(HeatmapMetric.Loss),
                    downloadMbps = heatmap.Typical(HeatmapMetric.Download),
                },
                cells = heatmap.Cells.Select(c => new
                {
                    day = c.Day.ToString(),
                    c.Hour,
                    c.Samples,
                    c.Lost,
                    c.LossPercent,
                    c.AvgLatencyMs,
                    c.SpeedTests,
                    c.AvgDownloadMbps,
                }),
            });
        });

        // Period of the report and the exports: ?days=30 (default), or ?from=yyyy-MM-dd&to=yyyy-MM-dd
        // (whole days in the server's time zone).
        api.MapGet("/report", async (ReportService reports, string? from, string? to, int? days,
            double? download, double? upload, CancellationToken ct) =>
        {
            if (!ReportPeriod.TryResolve(from, to, days, DateTimeOffset.UtcNow, TimeZoneInfo.Local, out var period, out var error))
            {
                return Results.BadRequest(new { error });
            }

            var report = await reports.BuildAsync(period, download, upload, ct);
            return Results.Ok(new
            {
                report.From,
                report.To,
                report.GeneratedAt,
                report.Targets,
                outages = report.Outages.Select(o => new { o.Outage.Target, o.Outage.Start, o.Outage.End, o.Outage.LostSamples, o.Cause }),
                internetOutages = report.InternetOutages.Count,
                internetDowntimeSeconds = Math.Round(report.InternetDowntime.TotalSeconds),
                speed = report.Speed,
                report.Download,
                report.Upload,
            });
        });

        var export = api.MapGroup("/export");

        // Each export comes as CSV (spreadsheets) and JSON (scripts, jq, pandas) with the same fields.
        export.MapGet("/samples.csv", (HttpContext http, ISampleStore store, string? from, string? to, int? days, string? target) =>
            Download(http, from, to, days, "samples", ExportFormat.Csv, (period, stream, ct) =>
                WriteCsvAsync(stream, writer => CsvExport.WriteSamplesAsync(writer, store.StreamAsync(period.From, period.To, target, ct), ct))));

        export.MapGet("/samples.json", (HttpContext http, ISampleStore store, string? from, string? to, int? days, string? target) =>
            Download(http, from, to, days, "samples", ExportFormat.Json, (period, stream, ct) =>
                JsonExport.WriteSamplesAsync(stream, store.StreamAsync(period.From, period.To, target, ct), ct)));

        foreach (var format in new[] { ExportFormat.Csv, ExportFormat.Json })
        {
            export.MapGet($"/outages.{format.Extension}", (HttpContext http, ReportService reports, TargetManager targets, string? from, string? to, int? days) =>
                Download(http, from, to, days, "outages", format, async (period, stream, ct) =>
                {
                    var outages = await reports.GetClassifiedOutagesAsync(period, await targets.GetEffectiveTargetsAsync(ct), ct);
                    await (format == ExportFormat.Json
                        ? JsonExport.WriteOutagesAsync(stream, outages, period.To, ct)
                        : WriteCsvAsync(stream, writer => CsvExport.WriteOutagesAsync(writer, outages, period.To)));
                }));

            export.MapGet($"/speedtests.{format.Extension}", (HttpContext http, ISpeedTestStore store, string? from, string? to, int? days) =>
                Download(http, from, to, days, "speedtests", format, async (period, stream, ct) =>
                {
                    var results = (await store.GetSinceAsync(period.From, ct)).Where(r => r.Timestamp < period.To).ToList();
                    await (format == ExportFormat.Json
                        ? JsonExport.WriteSpeedTestsAsync(stream, results, ct)
                        : WriteCsvAsync(stream, writer => CsvExport.WriteSpeedTestsAsync(writer, results)));
                }));
        }

        return api;
    }

    // Streams a download named e.g. "netpulse-samples-2026-10-14_2026-11-13.csv".
    private static IResult Download(HttpContext http, string? from, string? to, int? days, string name, ExportFormat format,
        Func<ReportPeriod, Stream, CancellationToken, Task> write)
    {
        if (!ReportPeriod.TryResolve(from, to, days, DateTimeOffset.UtcNow, TimeZoneInfo.Local, out var period, out var error))
        {
            return Results.BadRequest(new { error });
        }

        static string Day(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        http.Response.Headers.ContentDisposition =
            $"attachment; filename=\"netpulse-{name}-{Day(period.From)}_{Day(period.To)}.{format.Extension}\"";

        return Results.Stream(stream => write(period, stream, http.RequestAborted), format.ContentType);
    }

    // CSV goes through a UTF-8 (no BOM) text writer on top of the response stream.
    private static async Task WriteCsvAsync(Stream stream, Func<TextWriter, Task> write)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        await write(writer);
    }

    /// <summary>A download format of <c>/api/export</c>.</summary>
    private sealed record ExportFormat(string Extension, string ContentType)
    {
        public static readonly ExportFormat Csv = new("csv", "text/csv; charset=utf-8");
        public static readonly ExportFormat Json = new("json", "application/json; charset=utf-8");
    }
}
