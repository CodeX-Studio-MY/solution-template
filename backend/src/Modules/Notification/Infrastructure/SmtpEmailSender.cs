using Acme.Core.Configuration;
using Acme.Notification.Application;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Acme.Notification.Infrastructure;

/// <summary>SMTP through MailKit. Locally this is Mailpit, which catches every email in a web inbox.</summary>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string toAddress, string toName, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = textBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(settings.User))
            await client.AuthenticateAsync(settings.User, settings.Password ?? "", cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
