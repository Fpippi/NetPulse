using NetPulse.Core.Discovery;

namespace NetPulse.Tests.Discovery;

[TestFixture]
public class OuiDatabaseTests
{
    private const string Data = """
        # comment
        B827EB	Raspberry Pi Foundation
        70B3D5	IEEE Registration Authority
        70B3D5123	Small Vendor Ltd
        1C8259E	Medium Vendor Inc
        bad line
        """;

    private OuiDatabase _db = null!;

    [SetUp]
    public void SetUp() => _db = OuiDatabase.Load(new StringReader(Data));

    [Test]
    public void Loads_valid_lines_only()
    {
        Assert.That(_db.Count, Is.EqualTo(4));
    }

    [TestCase("b8:27:eb:12:34:56", "Raspberry Pi Foundation")]
    [TestCase("B8-27-EB-00-00-01", "Raspberry Pi Foundation")]
    [TestCase("70:B3:D5:12:3F:FF", "Small Vendor Ltd")]
    [TestCase("70:B3:D5:99:00:00", "IEEE Registration Authority")]
    [TestCase("1C:82:59:E0:00:01", "Medium Vendor Inc")]
    public void Finds_the_most_specific_vendor(string mac, string vendor)
    {
        Assert.That(_db.Lookup(mac), Is.EqualTo(vendor));
    }

    [TestCase("00:11:22:33:44:55")]
    [TestCase("not a mac")]
    [TestCase(null)]
    public void Returns_null_for_unknown_input(string? mac)
    {
        Assert.That(_db.Lookup(mac), Is.Null);
    }

    [Test]
    public void Randomized_addresses_have_no_vendor()
    {
        // BA:27:EB has the locally administered bit set, so it is not a Raspberry Pi.
        Assert.That(_db.Lookup("BA:27:EB:12:34:56"), Is.Null);
    }

    [Test]
    public void Bundled_database_is_valid()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "oui.txt");
        Assume.That(File.Exists(path), Is.True, "oui.txt is not copied to the test output");

        var db = OuiDatabase.LoadFile(path);

        Assert.That(db.Count, Is.GreaterThan(30_000));
        Assert.That(db.Lookup("B8:27:EB:00:00:00"), Does.Contain("Raspberry Pi"));
    }
}
