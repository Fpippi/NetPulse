using System.Security.Cryptography;
using System.Text;
using NetPulse.Core.Qr;

namespace NetPulse.Tests.Qr;

[TestFixture]
public class QrCodeTests
{
    // Expected matrices from an independent encoder (Project Nayuki's reference QR Code generator,
    // no level boost), as the first 16 hex digits of the SHA-256 of the rows ("0"/"1", one line per row).
    [TestCase("http://192.168.1.100:5080/phone", QrErrorCorrection.Medium, 0, 3, "266cd9f3fdc5cabb")]
    [TestCase("http://192.168.1.100:5080/phone", QrErrorCorrection.Medium, 2, 3, "f58d4b2087bb2309")]
    [TestCase("http://192.168.1.100:5080/phone", QrErrorCorrection.Medium, 7, 3, "1040d9be2c7d9b6b")]
    [TestCase("HELLO", QrErrorCorrection.Low, 3, 1, "eea957f1f3550546")]
    [TestCase("HELLO", QrErrorCorrection.Low, 6, 1, "9e0849ae101cdab2")]
    [TestCase("Città ✓", QrErrorCorrection.High, 5, 2, "a1d02c6140eb7a14")]
    [TestCase("https://example.com/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        QrErrorCorrection.Medium, 4, 6, "30a0f0722ab3409c")]
    [TestCase("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
        QrErrorCorrection.Quartile, 1, 8, "d29ff7facd54ef94")]
    public void Matches_the_reference_encoder(string text, QrErrorCorrection ecc, int mask, int version, string hash)
    {
        var qr = QrCode.Encode(Encoding.UTF8.GetBytes(text), ecc, mask);

        Assert.Multiple(() =>
        {
            Assert.That(qr.Version, Is.EqualTo(version));
            Assert.That(qr.Size, Is.EqualTo(version * 4 + 17));
            Assert.That(qr.Mask, Is.EqualTo(mask));
            Assert.That(Hash(qr), Is.EqualTo(hash));
        });
    }

    [Test]
    public void Version_10_with_two_block_sizes_matches_the_reference_encoder()
    {
        var text = "https://example.com/" + new string('b', 180);

        var qr = QrCode.Encode(Encoding.UTF8.GetBytes(text), QrErrorCorrection.Medium, mask: 5);

        Assert.That(qr.Version, Is.EqualTo(10));
        Assert.That(Hash(qr), Is.EqualTo("28e5f5870611a504"));
    }

    [TestCase("http://192.168.1.100:5080/phone", QrErrorCorrection.Medium, 2)]
    [TestCase("HELLO", QrErrorCorrection.Low, 0)]
    [TestCase("Città ✓", QrErrorCorrection.High, 2)]
    public void Picks_the_mask_with_the_lowest_penalty(string text, QrErrorCorrection ecc, int expectedMask) =>
        Assert.That(QrCode.Encode(text, ecc).Mask, Is.EqualTo(expectedMask));

    [Test]
    public void Draws_the_three_finder_patterns()
    {
        var qr = QrCode.Encode("NetPulse");

        foreach (var (cx, cy) in new[] { (3, 3), (qr.Size - 4, 3), (3, qr.Size - 4) })
        {
            Assert.Multiple(() =>
            {
                Assert.That(qr[cx, cy], Is.True, "centre");
                Assert.That(qr[cx - 3, cy], Is.True, "outer ring");
                Assert.That(qr[cx - 2, cy], Is.False, "light ring");
            });
        }
    }

    [Test]
    public void Outside_the_matrix_is_light()
    {
        var qr = QrCode.Encode("NetPulse");

        Assert.That(qr[-1, 0] || qr[0, -1] || qr[qr.Size, 0] || qr[0, qr.Size], Is.False);
    }

    [Test]
    public void Grows_with_the_payload()
    {
        Assert.That(QrCode.Encode("http://10.0.0.2:5080/phone").Version, Is.EqualTo(2));
        Assert.That(QrCode.Encode(new string('a', 150)).Version, Is.GreaterThan(5));
    }

    [Test]
    public void Rejects_text_longer_than_version_10()
    {
        var capacity = QrCode.Capacity(QrErrorCorrection.Medium);

        Assert.That(capacity, Is.EqualTo(213));
        Assert.That(() => QrCode.Encode(new string('a', capacity)), Throws.Nothing);
        Assert.That(() => QrCode.Encode(new string('a', capacity + 1)), Throws.ArgumentException);
    }

    [Test]
    public void Rejects_an_invalid_mask() =>
        Assert.That(() => QrCode.Encode("a"u8, QrErrorCorrection.Low, mask: 8), Throws.TypeOf<ArgumentOutOfRangeException>());

    private static string Hash(QrCode qr)
    {
        var rows = Enumerable.Range(0, qr.Size)
            .Select(y => string.Concat(Enumerable.Range(0, qr.Size).Select(x => qr[x, y] ? '1' : '0')));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows)));
        return Convert.ToHexStringLower(digest)[..16];
    }
}
