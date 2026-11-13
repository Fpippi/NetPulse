using NetPulse.Core.Models;
using NetPulse.Core.Storage;
using NetPulse.Core.Targets;

namespace NetPulse.Tests;

[TestFixture]
public class TargetManagerTests
{
    private static readonly string[] Configured = ["gateway", "1.1.1.1"];

    private FakeTargetStore _targets = null!;
    private FakeSampleStore _samples = null!;
    private TargetManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _targets = new FakeTargetStore();
        _samples = new FakeSampleStore();
        _manager = new TargetManager(Configured, _targets, _samples, maxCustomTargets: 2);
    }

    [Test]
    public async Task Adds_a_normalized_custom_target()
    {
        var result = await _manager.AddAsync("  Example.com ");

        Assert.That(result.Success, Is.True);
        Assert.That(result.Target, Is.EqualTo("example.com"));
        Assert.That(await _manager.GetEffectiveTargetsAsync(), Is.EqualTo(new[] { "gateway", "1.1.1.1", "example.com" }));
    }

    [Test]
    public async Task Reports_which_targets_are_custom()
    {
        await _manager.AddAsync("8.8.8.8");

        var targets = await _manager.GetTargetsAsync();

        Assert.That(targets, Is.EqualTo(new[]
        {
            new MonitoredTarget("gateway", false),
            new MonitoredTarget("1.1.1.1", false),
            new MonitoredTarget("8.8.8.8", true),
        }));
    }

    [Test]
    public async Task Rejects_invalid_input()
    {
        var result = await _manager.AddAsync("not a host");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(_targets.Items, Is.Empty);
    }

    [TestCase("1.1.1.1")]
    [TestCase("GATEWAY")]
    public async Task Rejects_targets_that_are_already_configured(string input)
    {
        var result = await _manager.AddAsync(input);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("already monitored"));
    }

    [Test]
    public async Task Rejects_duplicate_custom_targets()
    {
        await _manager.AddAsync("example.com");

        var result = await _manager.AddAsync("EXAMPLE.com");

        Assert.That(result.Success, Is.False);
        Assert.That(_targets.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Enforces_the_custom_target_limit()
    {
        await _manager.AddAsync("a.example");
        await _manager.AddAsync("b.example");

        var result = await _manager.AddAsync("c.example");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("at most 2"));
    }

    [Test]
    public async Task Removing_a_custom_target_deletes_its_samples()
    {
        await _manager.AddAsync("example.com");

        var removed = await _manager.RemoveAsync("example.com");

        Assert.That(removed, Is.True);
        Assert.That(_targets.Items, Is.Empty);
        Assert.That(_samples.DeletedTargets, Is.EqualTo(new[] { "example.com" }));
    }

    [Test]
    public async Task Configured_targets_cannot_be_removed()
    {
        Assert.That(await _manager.RemoveAsync("1.1.1.1"), Is.False);
        Assert.That(await _manager.RemoveAsync("unknown.example"), Is.False);
        Assert.That(_samples.DeletedTargets, Is.Empty);
    }

    [Test]
    public async Task Raises_changed_after_add_and_remove()
    {
        var count = 0;
        _manager.Changed += () => count++;

        await _manager.AddAsync("example.com");
        await _manager.AddAsync("invalid host");
        await _manager.RemoveAsync("example.com");

        Assert.That(count, Is.EqualTo(2));
    }

    private sealed class FakeTargetStore : ITargetStore
    {
        public List<string> Items { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Items.ToList());

        public Task<bool> AddAsync(string target, CancellationToken cancellationToken = default)
        {
            if (Items.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(false);
            }

            Items.Add(target);
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string target, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.RemoveAll(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase)) > 0);
    }

    private sealed class FakeSampleStore : ISampleStore
    {
        public List<string> DeletedTargets { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddRangeAsync(IEnumerable<PingSample> samples, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PingSample>> GetSinceAsync(
            DateTimeOffset since, string? target = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PingSample>>([]);

        public IAsyncEnumerable<PingSample> StreamAsync(
            DateTimeOffset from, DateTimeOffset to, string? target = null, CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<PingSample>();

        public Task<int> PurgeOlderThanAsync(DateTimeOffset before, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> DeleteTargetAsync(string target, CancellationToken cancellationToken = default)
        {
            DeletedTargets.Add(target);
            return Task.FromResult(0);
        }
    }
}
