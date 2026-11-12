using NetPulse.Core.Targets;

namespace NetPulse.Tests;

[TestFixture]
public class TargetListTests
{
    [Test]
    public void Keeps_configured_first_and_removes_duplicates()
    {
        var merged = TargetList.Merge(["gateway", "1.1.1.1"], ["example.com", "1.1.1.1", "GATEWAY", " "]);

        Assert.That(merged, Is.EqualTo(new[] { "gateway", "1.1.1.1", "example.com" }));
    }
}
