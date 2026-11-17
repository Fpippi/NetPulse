using System.Text;
using System.Text.Json;
using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.Reports;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.Reports;

[TestFixture]
public class JsonExportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Writes_samples_as_an_array_of_flat_objects()
    {
        var json = await Write(s => JsonExport.WriteSamplesAsync(s, Async(
            new PingSample(T0, "gateway", true, 1.254),
            new PingSample(T0.AddSeconds(5), "2606:4700:4700::1111", false, null))));

        Assert.That(json, Is.EqualTo(
            """[{"timestamp_utc":"2026-11-12T09:00:00.000Z","target":"gateway","success":true,"latency_ms":1.25},""" +
            """{"timestamp_utc":"2026-11-12T09:00:05.000Z","target":"2606:4700:4700::1111","success":false,"latency_ms":null}]"""));
    }

    [Test]
    public async Task No_records_is_an_empty_array() =>
        Assert.That(await Write(s => JsonExport.WriteSamplesAsync(s, Async())), Is.EqualTo("[]"));

    [Test]
    public async Task Streams_many_samples()
    {
        var samples = Enumerable.Range(0, 2_001).Select(i => new PingSample(T0.AddSeconds(i), "1.1.1.1", true, i)).ToArray();

        using var doc = JsonDocument.Parse(await Write(s => JsonExport.WriteSamplesAsync(s, Async(samples))));

        Assert.That(doc.RootElement.GetArrayLength(), Is.EqualTo(2_001));
        Assert.That(doc.RootElement[2_000].GetProperty("latency_ms").GetDouble(), Is.EqualTo(2_000));
    }

    [Test]
    public async Task Writes_outages_with_duration_cause_and_null_end()
    {
        var ended = new Outage("1.1.1.1", T0, T0.AddSeconds(10), T0.AddSeconds(15), 3);
        var ongoing = new Outage("gateway", T0.AddMinutes(1), T0.AddMinutes(2), null, 24);

        var json = await Write(s => JsonExport.WriteOutagesAsync(s,
            [new(ended, OutageCause.Internet), new(ongoing, OutageCause.LocalNetwork)], T0.AddMinutes(3)));

        Assert.That(json, Is.EqualTo(
            """[{"start_utc":"2026-11-12T09:00:00.000Z","end_utc":"2026-11-12T09:00:15.000Z","target":"1.1.1.1","duration_seconds":15,"lost_pings":3,"ongoing":false,"cause":"Internet"},""" +
            """{"start_utc":"2026-11-12T09:01:00.000Z","end_utc":null,"target":"gateway","duration_seconds":120,"lost_pings":24,"ongoing":true,"cause":"LocalNetwork"}]"""));
    }

    [Test]
    public async Task Writes_speed_tests_with_bufferbloat_and_errors()
    {
        var json = await Write(s => JsonExport.WriteSpeedTestsAsync(s,
        [
            new SpeedTestResult(T0, "speed.example", 12.346, 98.7654, 20.1, 25_000_000, 10_000_000, null,
                new Bufferbloat(12, 55.5, 20)),
            new SpeedTestResult(T0.AddHours(6), "speed.example", null, null, null, 0, 0, "Timed out, \"retry\""),
        ]));

        using var doc = JsonDocument.Parse(json);
        var ok = doc.RootElement[0];
        var failed = doc.RootElement[1];

        Assert.Multiple(() =>
        {
            Assert.That(ok.GetProperty("download_mbps").GetDouble(), Is.EqualTo(98.77));
            Assert.That(ok.GetProperty("latency_ms").GetDouble(), Is.EqualTo(12.35));
            Assert.That(ok.GetProperty("download_bytes").GetInt64(), Is.EqualTo(25_000_000));
            Assert.That(ok.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(ok.GetProperty("bufferbloat_grade").GetString(), Is.EqualTo("B"));
            Assert.That(failed.GetProperty("download_mbps").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(failed.GetProperty("error").GetString(), Is.EqualTo("Timed out, \"retry\""));
            Assert.That(failed.GetProperty("bufferbloat_grade").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task Has_the_same_fields_as_the_csv()
    {
        var csv = new StringWriter();
        await CsvExport.WriteSpeedTestsAsync(csv, [new SpeedTestResult(T0, "s", 1, 1, 1, 1, 1, null)]);
        var json = await Write(s => JsonExport.WriteSpeedTestsAsync(s, [new SpeedTestResult(T0, "s", 1, 1, 1, 1, 1, null)]));

        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement[0].EnumerateObject().Select(p => p.Name);

        Assert.That(names, Is.EqualTo(csv.ToString().Split("\r\n")[0].Split(',')));
    }

    private static async Task<string> Write(Func<Stream, Task> write)
    {
        using var stream = new MemoryStream();
        await write(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async IAsyncEnumerable<PingSample> Async(params PingSample[] samples)
    {
        foreach (var s in samples)
        {
            yield return s;
        }

        await Task.CompletedTask;
    }
}
