using System.Net;
using System.Net.Mail;

namespace UniversalITMonitor.Application.Notifications;

public sealed class EmailNotificationSender : IEmailNotificationSender
{
    public async Task<NotificationTransportResult> SendAsync(
        UniversalITMonitor.Core.Models.NotificationSettings settings,
        string? userName,
        string? password,
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
            string.IsNullOrWhiteSpace(settings.SmtpFromAddress) ||
            string.IsNullOrWhiteSpace(settings.SmtpToAddress))
        {
            return new(false, "Email is not fully configured.");
        }

        using var mail = new MailMessage(settings.SmtpFromAddress, settings.SmtpToAddress, message.Subject, message.Body);
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.SmtpUseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 8000
        };
        client.Credentials = string.IsNullOrWhiteSpace(userName)
            ? null
            : new NetworkCredential(userName, password ?? string.Empty);
        await client.SendMailAsync(mail, cancellationToken);
        return new(true, "Sent.");
    }
}
