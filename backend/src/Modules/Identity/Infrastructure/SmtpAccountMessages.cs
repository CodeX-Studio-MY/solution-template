using System.Globalization;
using Saaskit.Core.Email;
using Saaskit.Identity.Application;
using Microsoft.Extensions.Logging;

namespace Saaskit.Identity.Infrastructure;

/// <summary>
/// The default <see cref="IAccountMessages"/>: a plain-text email with the single-use link. The text is English only, like the sign-in pages.
/// The link is never logged; a failed send is, without it.
/// </summary>
internal sealed partial class SmtpAccountMessages(IEmailSender email, ILogger<SmtpAccountMessages> logger) : IAccountMessages
{
    public async Task<bool> SendAsync(AccountLink link, CancellationToken cancellationToken = default)
    {
        var (subject, intro) = link.Kind == "invitation"
            ? ($"You're invited to {link.TenantName}", $"You have been invited to {link.TenantName}. Open this link to choose your password:")
            : ("Reset your password", $"Someone asked to reset the password of your {link.TenantName} account. Open this link to choose a new one:");
        var expires = link.ExpiresOn.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var body = $"""
            Hello {link.DisplayName},

            {intro}

            {link.Url}

            The link works once and expires on {expires}.
            If you did not expect this email, you can ignore it.
            """;
        try
        {
            await email.SendAsync(link.Email, link.DisplayName, subject, body, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSendFailed(logger, ex, link.Kind);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send the {Kind} email")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string kind);
}
