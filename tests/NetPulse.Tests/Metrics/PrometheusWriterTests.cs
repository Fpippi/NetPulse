using NetPulse.Core.Metrics;

namespace NetPulse.Tests.Metrics;

[TestFixture]
public class PrometheusWriterTests
{
    [Test]
    public void Declares_each_family_once_then_writes_samples()
    {
        var text = new PrometheusWriter()
            .Gauge("x_ms", "Some help", 1.5, ("target", "a"))
            .Gauge("x_ms", "Some help", 2, ("target", "b"))
            .ToString();

        Assert.That(text, Is.EqualTo(
            "# HELP x_ms Some help\n" +
            "# TYPE x_ms gauge\n" +
            "x_ms{target=\"a\"} 1.5\n" +
            "x_ms{target=\"b\"} 2\n"));
    }

    [Test]
    public void Skips_null_values_without_declaring_the_family()
    {
        Assert.That(new PrometheusWriter().Gauge("x", "h", null).ToString(), Is.Empty);
    }

    [Test]
    public void Escapes_label_values()
    {
        Assert.That(PrometheusWriter.EscapeLabel("a\"b\\c\nd"), Is.EqualTo("a\\\"b\\\\c\\nd"));
    }

    [Test]
    [SetCulture("it-IT")]
    public void Numbers_use_a_dot_whatever_the_culture()
    {
        Assert.That(PrometheusWriter.Format(1234.5), Is.EqualTo("1234.5"));
        Assert.That(PrometheusWriter.Format(double.PositiveInfinity), Is.EqualTo("+Inf"));
    }

    [Test]
    public void Metrics_without_labels()
    {
        Assert.That(new PrometheusWriter().Gauge("up", "h", 1).ToString(), Does.EndWith("\nup 1\n"));
    }
}
