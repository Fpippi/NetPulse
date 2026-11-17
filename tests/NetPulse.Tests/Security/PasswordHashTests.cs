using NetPulse.Core.Security;

namespace NetPulse.Tests.Security;

[TestFixture]
public class PasswordHashTests
{
    // Few iterations keep the tests fast; the format and the check are the same.
    private const int FastIterations = 1_000;

    [Test]
    public void A_created_hash_verifies_only_its_own_password()
    {
        var hash = PasswordHash.Create("correct horse", FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(PasswordHash.IsHash(hash), Is.True);
            Assert.That(PasswordHash.IsValidHash(hash), Is.True);
            Assert.That(PasswordHash.Verify("correct horse", hash), Is.True);
            Assert.That(PasswordHash.Verify("correct horse ", hash), Is.False);
            Assert.That(PasswordHash.Verify("Correct horse", hash), Is.False);
        });
    }

    [Test]
    public void Each_hash_gets_its_own_salt() =>
        Assert.That(PasswordHash.Create("secret", FastIterations), Is.Not.EqualTo(PasswordHash.Create("secret", FastIterations)));

    [Test]
    public void The_format_records_the_iterations()
    {
        var parts = PasswordHash.Create("secret", FastIterations).Split('$');

        Assert.That(parts, Has.Length.EqualTo(4));
        Assert.That(parts[0], Is.EqualTo("pbkdf2-sha256"));
        Assert.That(parts[1], Is.EqualTo("1000"));
    }

    [Test]
    public void A_plain_configured_password_is_compared_as_is()
    {
        Assert.That(PasswordHash.Verify("hunter2", "hunter2"), Is.True);
        Assert.That(PasswordHash.Verify("hunter", "hunter2"), Is.False);
        Assert.That(PasswordHash.IsHash("hunter2"), Is.False);
    }

    [TestCase(null, "x")]
    [TestCase("", "x")]
    [TestCase("x", null)]
    [TestCase("x", "")]
    public void Empty_values_never_match(string? password, string? configured) =>
        Assert.That(PasswordHash.Verify(password, configured), Is.False);

    [TestCase("pbkdf2-sha256$")]
    [TestCase("pbkdf2-sha256$abc$AAAAAAAAAAA=$AAAAAAAAAAAAAAAAAAAAAA==")]
    [TestCase("pbkdf2-sha256$0$AAAAAAAAAAA=$AAAAAAAAAAAAAAAAAAAAAA==")]
    [TestCase("pbkdf2-sha256$99999999999$AAAAAAAAAAA=$AAAAAAAAAAAAAAAAAAAAAA==")]
    [TestCase("pbkdf2-sha256$1000$not base64$AAAAAAAAAAAAAAAAAAAAAA==")]
    [TestCase("pbkdf2-sha256$1000$AAAA$AAAAAAAAAAAAAAAAAAAAAA==")]
    [TestCase("pbkdf2-sha256$1000$AAAAAAAAAAA=")]
    public void Malformed_hashes_never_match(string configured)
    {
        Assert.That(PasswordHash.IsValidHash(configured), Is.False);
        Assert.That(PasswordHash.Verify("anything", configured), Is.False);
    }

    [Test]
    public void Fixed_time_equality_handles_different_lengths()
    {
        Assert.That(PasswordHash.FixedTimeEquals("token", "token"), Is.True);
        Assert.That(PasswordHash.FixedTimeEquals("token", "token-longer"), Is.False);
    }
}
