namespace Saaskit.Core.Email;

/// <summary>Sends email. The default adapter speaks SMTP (Mailpit locally). Templates are localized per tenant by the caller.</summary>
public interface IEmailSender
{
    Task SendAsync(string toAddress, string toName, string subject, string textBody, CancellationToken cancellationToken = default);
}
