using NetPulse.Core.Reports;

namespace NetPulse.Tests.Reports;

[TestFixture]
public class CsvTests
{
    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("1.1.1.1", "1.1.1.1")]
    [TestCase("a,b", "\"a,b\"")]
    [TestCase("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [TestCase("two\nlines", "\"two\nlines\"")]
    public void Escapes_fields_only_when_needed(string? value, string expected)
    {
        Assert.That(Csv.Escape(value), Is.EqualTo(expected));
    }

    [Test]
    public void Rows_end_with_crlf()
    {
        Assert.That(Csv.Row("a", null, "c,d"), Is.EqualTo("a,,\"c,d\"\r\n"));
    }

    [Test]
    public void Formats_timestamps_as_utc_iso_8601()
    {
        var local = new DateTimeOffset(2026, 11, 12, 17, 31, 25, 888, TimeSpan.FromHours(1));

        Assert.That(Csv.Timestamp(local), Is.EqualTo("2026-11-12T16:31:25.888Z"));
        Assert.That(Csv.Timestamp((DateTimeOffset?)null), Is.Empty);
    }

    [Test]
    [SetCulture("it-IT")]
    public void Formats_numbers_with_a_dot_on_any_culture()
    {
        Assert.That(Csv.Number(12.3456), Is.EqualTo("12.35"));
        Assert.That(Csv.Number(1234567L), Is.EqualTo("1234567"));
        Assert.That(Csv.Number((double?)null), Is.Empty);
    }
}
