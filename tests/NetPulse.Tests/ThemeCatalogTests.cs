using NetPulse.Core.Appearance;

namespace NetPulse.Tests;

[TestFixture]
public class ThemeCatalogTests
{
    [Test]
    public void Offers_six_themes_with_unique_ids()
    {
        var ids = ThemeCatalog.All.Select(t => t.Id).ToList();

        Assert.That(ids, Is.EqualTo(new[]
        {
            "dark", "light", "cyberpunk", "anime", "sakura",
            "bizarre", "bizarre-night", "bizarre-pastel", "bizarre-desert", "bizarre-ink",
            "medieval",
        }));
        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void Default_is_dark() => Assert.That(ThemeCatalog.Default, Is.SameAs(ThemeCatalog.Dark));

    [TestCase("light", "light")]
    [TestCase("CYBERPUNK", "cyberpunk")]
    [TestCase(" anime ", "anime")]
    [TestCase("Bizarre", "bizarre")]
    [TestCase("medieval", "medieval")]
    [TestCase("Sakura", "sakura")]
    [TestCase("BIZARRE-INK", "bizarre-ink")]
    public void Resolves_known_ids(string input, string expected) =>
        Assert.That(ThemeCatalog.Resolve(input).Id, Is.EqualTo(expected));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("vaporwave")]
    [TestCase("\"><script>")]
    public void Falls_back_to_the_default_for_unknown_ids(string? input) =>
        Assert.That(ThemeCatalog.Resolve(input), Is.SameAs(ThemeCatalog.Default));

    [TestCase("/", "/")]
    [TestCase("/speed", "/speed")]
    [TestCase("/report?days=7", "/report?days=7")]
    public void Keeps_local_return_paths(string input, string expected) =>
        Assert.That(ThemeCatalog.SafeReturnPath(input), Is.EqualTo(expected));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("speed")]
    [TestCase("https://evil.example/")]
    [TestCase("//evil.example/")]
    [TestCase("/\\evil.example/")]
    [TestCase("/speed\r\nSet-Cookie: x=1")]
    public void Rejects_anything_that_could_leave_the_site(string? input) =>
        Assert.That(ThemeCatalog.SafeReturnPath(input), Is.EqualTo("/"));
}
