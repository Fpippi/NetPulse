using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NetPulse.Core.Models;
using NetPulse.Core.Storage;
using NetPulse.Web.Backup;

namespace NetPulse.Web.Tests.Backup;

[TestFixture]
public class DatabaseBackupTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private NetPulseFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NetPulseFactory();
        _ = _factory.Services; // builds the host and creates the schema
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private DatabaseBackup Backup => _factory.Services.GetRequiredService<DatabaseBackup>();

    private ISampleStore Samples => _factory.Services.GetRequiredService<ISampleStore>();

    private async Task AddSamplesAsync(string target, int count) =>
        await Samples.AddRangeAsync(Enumerable.Range(0, count)
            .Select(i => new PingSample(Start.AddSeconds(i * 5), target, true, 10 + i)));

    private async Task<IReadOnlyList<PingSample>> AllSamplesAsync() =>
        await Samples.GetSinceAsync(Start.AddDays(-1));

    [Test]
    public async Task Restores_a_backup_and_keeps_a_safety_copy()
    {
        await AddSamplesAsync("before", 3);
        var snapshot = await Backup.CreateSnapshotAsync();
        await AddSamplesAsync("after", 2);

        RestoreResult result;
        await using (var file = File.OpenRead(snapshot))
        {
            result = await Backup.RestoreAsync(file);
        }

        File.Delete(snapshot);
        Assert.Multiple(async () =>
        {
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Samples, Is.EqualTo(3));
            Assert.That(result.From, Is.EqualTo(Start));
            Assert.That(File.Exists(result.SafetyCopy), Is.True);
            Assert.That((await AllSamplesAsync()).Select(s => s.Target).Distinct(), Is.EqualTo(new[] { "before" }));
        });
    }

    [Test]
    public async Task Refuses_a_file_that_is_not_a_database()
    {
        await AddSamplesAsync("live", 2);

        var result = await Backup.RestoreAsync(new MemoryStream("hello"u8.ToArray()));

        Assert.That(result.Error, Does.Contain("not a database"));
        Assert.That(await AllSamplesAsync(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Puts_the_previous_data_back_when_the_backup_cannot_be_upgraded()
    {
        await AddSamplesAsync("live", 4);

        // The required tables are there, so the file passes the checks, but "samples" has none of
        // the columns NetPulse needs: creating the timestamp index fails after the copy.
        var bad = Path.Combine(_factory.Folder, "wrong-columns.db");
        await using (var connection = new SqliteConnection($"Data Source={bad};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE samples (id INTEGER PRIMARY KEY, value TEXT);
                CREATE TABLE outages (id INTEGER PRIMARY KEY);
                CREATE TABLE speed_tests (id INTEGER PRIMARY KEY);
                INSERT INTO samples (value) VALUES ('x');
                """;
            await create.ExecuteNonQueryAsync();
        }

        RestoreResult result;
        await using (var file = File.OpenRead(bad))
        {
            result = await Backup.RestoreAsync(file);
        }

        Assert.Multiple(async () =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("previous data was kept"));
            var samples = await AllSamplesAsync();
            Assert.That(samples, Has.Count.EqualTo(4));
            Assert.That(samples.Select(s => s.Target).Distinct(), Is.EqualTo(new[] { "live" }));
        });

        // And the database still works afterwards.
        await AddSamplesAsync("later", 1);
        Assert.That(await AllSamplesAsync(), Has.Count.EqualTo(5));
    }
}
