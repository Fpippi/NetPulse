using System.Text;
using NetPulse.Core.Backup;

namespace NetPulse.Tests.Backup;

[TestFixture]
public class BackupFileTests
{
    [Test]
    public void Names_the_file_after_the_local_time()
    {
        var rome = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

        var name = BackupFile.FileName(new DateTimeOffset(2026, 11, 19, 23, 30, 0, TimeSpan.Zero), rome);

        Assert.That(name, Is.EqualTo("netpulse-backup-2026-11-20-0030.db"));
    }

    [Test]
    public void Recognizes_the_sqlite_header()
    {
        var database = Encoding.ASCII.GetBytes("SQLite format 3\0").Concat(new byte[84]).ToArray();

        Assert.That(BackupFile.HasSqliteHeader(database), Is.True);
        Assert.That(BackupFile.HasSqliteHeader("PK\u0003\u0004 a zip file"u8), Is.False);
        Assert.That(BackupFile.HasSqliteHeader("SQLite"u8), Is.False);
        Assert.That(BackupFile.HasSqliteHeader([]), Is.False);
    }

    [Test]
    public void Accepts_a_netpulse_database_even_from_an_older_version()
    {
        Assert.That(BackupFile.Validate("ok", ["samples", "outages", "speed_tests", "sqlite_sequence"]), Is.Null);
    }

    [Test]
    public void Refuses_a_damaged_file()
    {
        Assert.That(BackupFile.Validate("*** in database main ***\nPage 12: btreeInitPage() returns error code 11", ["samples"]),
            Does.Contain("damaged"));
        Assert.That(BackupFile.Validate(null, ["samples"]), Does.Contain("damaged"));
    }

    [Test]
    public void Refuses_another_database()
    {
        Assert.That(BackupFile.Validate("ok", ["users", "samples"]),
            Is.EqualTo("This is not a NetPulse database (missing outages, speed_tests)."));
    }
}
