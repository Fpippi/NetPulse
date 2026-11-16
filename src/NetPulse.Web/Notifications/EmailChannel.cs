using System.Net;
using System.Net.Mail;
using NetPulse.Core.Notifications;

namespace NetPulse.Web.Notifications;

/// <summary>Alerts by e-mail through SMTP (System.Net.Mail, no extra dependency).</summary>
public sealed class EmailChannel(SmtpOptions options) : INotificationChannel
{
    public string Name => "email";

    public async Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl };
        if (!string.IsNullOrWhiteSpace(options.User))
        {
            client.Credentials = new NetworkCredential(options.User, options.Password);
        }

        var from = string.IsNullOrWhiteSpace(options.From) ? options.User : options.From;
        using var message = new MailMessage
        {
            From = new MailAddress(from, "NetPulse"),
            Subject = "[NetPulse] " + notification.Title,
            Body = notification.Message + "\n\n— NetPulse",
        };

        foreach (var to in options.To.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            message.To.Add(to);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
