using NetPulse.Core.Qr;

namespace NetPulse.Tests.Qr;

[TestFixture]
public class QrSvgTests
{
    [Test]
    public void View_box_includes_the_quiet_zone() =>
        Assert.That(QrSvg.ViewBox(QrCode.Encode("HELLO", QrErrorCorrection.Low)), Is.EqualTo("0 0 29 29"));

    [Test]
    public void Merges_horizontal_runs_and_offsets_by_the_quiet_zone()
    {
        var path = QrSvg.PathData(QrCode.Encode("HELLO", QrErrorCorrection.Low));

        // Top row of version 1: finder (7 dark), separator, then the top-right finder (7 dark).
        Assert.That(path, Does.StartWith("M4 4h7v1h-7z"));
        Assert.That(path, Does.Contain("M18 4h7v1h-7z"));
    }

    [Test]
    public void Document_is_dark_on_light()
    {
        var svg = QrSvg.Document(QrCode.Encode("HELLO"));

        Assert.That(svg, Does.StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 29 29\""));
        Assert.That(svg, Does.Contain("fill=\"#fff\"").And.Contain("fill=\"#000\""));
    }
}
