using NetPulse.Core.Models;
using NetPulse.Core.Outages;
using NetPulse.Core.Reports;
using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.Reports;

[TestFixture]
public class CsvExportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Writes_samples_with_a_header()
    {
        var writer = new StringWriter();

        await CsvExport.WriteSamplesAsync(writer, Async(
            new PingSample(T0, "gateway", true, 1.25),
            new PingSample(T0.AddSeconds(5), "1.1.1.1", false, null)));

        Assert.That(writer.ToString(), Is.EqualTo(
            "timestamp_utc,target,success,latency_ms\r\n" +
            "2026-11-12T09:00:00.000Z,gateway,true,1.25\r\n" +
            "2026-11-12T09:00:05.000Z,1.1.1.1,false,\r\n"));
    }

    [Test]
    public async Task Writes_outages_with_duration_and_cause()
    {
        var writer = new StringWriter();
        var ended = new Outage("1.1.1.1", T0, T0.AddSeconds(10), T0.AddSeconds(15), 3);
        var ongoing = new Outage("gateway", T0.AddMinutes(1), T0.AddMinutes(2), null, 24);

        await CsvExport.WriteOutagesAsync(writer,
            [new(ended, OutageCause.Internet), new(ongoing, OutageCause.LocalNetwork)], T0.AddMinutes(3));

        Assert.That(writer.ToString(), Is.EqualTo(
            "start_utc,end_utc,target,duration_seconds,lost_pings,ongoing,cause\r\n" +
            "2026-11-12T09:00:00.000Z,2026-11-12T09:00:15.000Z,1.1.1.1,15,3,false,Internet\r\n" +
            "2026-11-12T09:01:00.000Z,,gateway,120,24,true,LocalNetwork\r\n"));
    }

    [Test]
    public async Task Writes_speed_tests_and_quotes_error_messages()
    {
        var writer = new StringWriter();

        await CsvExport.WriteSpeedTestsAsync(writer,
        [
            new SpeedTestResult(T0, "speed.example", 12.346, 98.7654, 20.1, 25_000_000, 10_000_000, null),
            new SpeedTestResult(T0.AddHours(6), "speed.example", null, null, null, 0, 0, "Timed out, retry"),
        ]);

        Assert.That(writer.ToString(), Is.EqualTo(
            "timestamp_utc,server,download_mbps,upload_mbps,latency_ms,download_bytes,upload_bytes,error\r\n" +
            "2026-11-12T09:00:00.000Z,speed.example,98.77,20.1,12.35,25000000,10000000,\r\n" +
            "2026-11-12T15:00:00.000Z,speed.example,,,,0,0,\"Timed out, retry\"\r\n"));
    }

    private static async IAsyncEnumerable<PingSample> Async(params PingSample[] samples)
    {
        foreach (var sample in samples)
        {
            await Task.Yield();
            yield return sample;
        }
    }
}
