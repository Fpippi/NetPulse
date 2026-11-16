using System.Text.Json;
using NetPulse.Core.Notifications;

namespace NetPulse.Tests.Notifications;

[TestFixture]
public class ChannelRequestTests
{
    private static readonly Notification Down = new(
        new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero),
        NotificationKind.OutageStarted, NotificationSeverity.Problem, "1.1.1.1 è giù", "Not answering.");

    private static async Task<JsonElement> Json(HttpRequestMessage request) =>
        JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;

    [Test]
    public async Task Ntfy_posts_json_to_the_server_root_with_the_topic()
    {
        using var request = NtfyChannel.BuildRequest(new Uri("https://ntfy.example/sub/my-topic"), "tk_secret", Down);
        var json = await Json(request);

        Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://ntfy.example/sub/")));
        Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer tk_secret"));
        Assert.That(json.GetProperty("topic").GetString(), Is.EqualTo("my-topic"));
        Assert.That(json.GetProperty("title").GetString(), Is.EqualTo("1.1.1.1 è giù"), "non-ASCII is fine in JSON");
        Assert.That(json.GetProperty("priority").GetInt32(), Is.EqualTo(4));
    }

    [Test]
    public void Ntfy_without_token_sends_no_authorization()
    {
        using var request = NtfyChannel.BuildRequest(new Uri("https://ntfy.sh/topic"), null, Down);

        Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://ntfy.sh/")));
        Assert.That(request.Headers.Authorization, Is.Null);
    }

    [Test]
    public async Task Telegram_calls_send_message_for_the_chat()
    {
        using var request = TelegramChannel.BuildRequest("123:abc", "-10042", Down);
        var json = await Json(request);

        Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://api.telegram.org/bot123:abc/sendMessage")));
        Assert.That(json.GetProperty("chat_id").GetString(), Is.EqualTo("-10042"));
        Assert.That(json.GetProperty("text").GetString(), Is.EqualTo("🔴 1.1.1.1 è giù\nNot answering."));
        Assert.That(json.TryGetProperty("parse_mode", out _), Is.False, "plain text, nothing to escape");
    }

    [Test]
    public async Task Webhook_payload_works_for_scripts_slack_and_discord()
    {
        using var request = WebhookChannel.BuildRequest(new Uri("https://hooks.example/abc"), Down);
        var json = await Json(request);

        Assert.That(json.GetProperty("kind").GetString(), Is.EqualTo("OutageStarted"));
        Assert.That(json.GetProperty("severity").GetString(), Is.EqualTo("Problem"));
        Assert.That(json.GetProperty("timestamp").GetDateTimeOffset(), Is.EqualTo(Down.Timestamp));
        Assert.That(json.GetProperty("text").GetString(), Is.EqualTo("1.1.1.1 è giù\nNot answering."));
        Assert.That(json.GetProperty("content").GetString(), Is.EqualTo(json.GetProperty("text").GetString()));
    }

    [Test]
    public async Task Webhook_trims_content_to_the_discord_limit()
    {
        var longOne = Down with { Message = new string('x', 3000) };
        using var request = WebhookChannel.BuildRequest(new Uri("https://hooks.example/abc"), longOne);

        Assert.That((await Json(request)).GetProperty("content").GetString(), Has.Length.EqualTo(2000));
    }
}
