using Farm360.Identity.Application;
using Microsoft.Extensions.Logging;

namespace Farm360.Identity.Infrastructure;

/// <summary>
/// The default <see cref="IAccountMessages"/> until the messaging layer exists: logs the link at Information level so
/// local development can use it (<c>scripts/dev-up</c> prints invitation links). Link tokens are single-use and short-lived.
/// </summary>
internal sealed partial class LoggingAccountMessages(ILogger<LoggingAccountMessages> logger) : IAccountMessages
{
    public Task SendAsync(AccountLink link, CancellationToken cancellationToken = default)
    {
        LogLink(logger, link.Kind, link.Email, link.Url);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account link ({Kind}) for {Email}: {Url}")]
    private static partial void LogLink(ILogger logger, string kind, string email, Uri url);
}
