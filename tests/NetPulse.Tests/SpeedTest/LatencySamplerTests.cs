using NetPulse.Core.SpeedTest;

namespace NetPulse.Tests.SpeedTest;

[TestFixture]
public class LatencySamplerTests
{
    [Test]
    public async Task Takes_the_requested_number_of_samples_and_skips_lost_ones()
    {
        var values = new Queue<double?>([10, null, 12]);
        var sampler = new LatencySampler(_ => Task.FromResult(values.Dequeue()), TimeSpan.Zero);
        var into = new List<double>();

        await sampler.SampleAsync(3, into);

        Assert.That(into, Is.EqualTo(new[] { 10.0, 12.0 }));
    }

    [Test]
    public async Task Samples_while_the_work_runs_and_stops_after()
    {
        var calls = 0;
        var sampler = new LatencySampler(_ => Task.FromResult<double?>(++calls), TimeSpan.FromMilliseconds(5));
        var into = new List<double>();

        var result = await sampler.WhileAsync(async () =>
        {
            await Task.Delay(100);
            return "done";
        }, into);
        var countAfter = into.Count;
        await Task.Delay(50);

        Assert.That(result, Is.EqualTo("done"));
        Assert.That(countAfter, Is.GreaterThan(1));
        Assert.That(into, Has.Count.EqualTo(countAfter), "no samples once the work has finished");
    }

    [Test]
    public void Keeps_samples_when_the_work_fails()
    {
        var sampler = new LatencySampler(_ => Task.FromResult<double?>(7), TimeSpan.FromMilliseconds(5));
        var into = new List<double>();

        Assert.ThrowsAsync<IOException>(() => sampler.WhileAsync<int>(async () =>
        {
            await Task.Delay(30);
            throw new IOException("connection reset");
        }, into));
        Assert.That(into, Is.Not.Empty);
    }
}
