using Saaskit.Core.Http;
using Saaskit.Core.Security;
using Saaskit.Core.Tenancy;
using Saaskit.Notification.Domain;
using Microsoft.Extensions.Logging;

namespace Saaskit.Notification.Application;

/// <summary>Creates notifications (history, real-time push, email) and answers the signed-in person's reads.</summary>
public sealed partial class NotificationService(
    INotificationStore store,
    INotificationPusher pusher,
    IEmailSender email,
    ITenantContext tenant,
    ICurrentPrincipal principal,
    TimeProvider time,
    ILogger<NotificationService> logger)
{
    /// <summary>Tells one person about something, with text the caller already localized: saved, pushed in real time and emailed.</summary>
    public async Task DeliverAsync(Contact recipient, string kind, string title, string body, string emailSubject, string? link)
    {
        var notification = store.Add(new UserNotification(recipient.Id, kind, title, body, link));
        await store.SaveAsync();

        await pusher.PushAsync(tenant.Tenant.Id, recipient.Id, ToDto(notification));

        if (!string.IsNullOrWhiteSpace(recipient.Email))
        {
            try
            {
                await email.SendAsync(recipient.Email, recipient.DisplayName, emailSubject, body);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The notification is saved and pushed; a mail server problem must not make the event fail and retry forever.
                LogEmailFailed(logger, ex, kind);
            }
        }
    }

    public async Task<Page<NotificationDto>> ListAsync(PageRequest page, bool unreadOnly, CancellationToken cancellationToken = default) =>
        (await store.ListAsync(CurrentUser, unreadOnly, page, cancellationToken)).Map(ToDto);

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken = default) => store.UnreadCountAsync(CurrentUser, cancellationToken);

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await store.FindAsync(id, CurrentUser, cancellationToken) ?? throw new Core.Errors.NotFoundException();
        notification.MarkRead(time.GetUtcNow().UtcDateTime);
        await store.SaveAsync(cancellationToken);
    }

    public Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default) =>
        store.MarkAllReadAsync(CurrentUser, time.GetUtcNow().UtcDateTime, cancellationToken);

    private Guid CurrentUser => Guid.TryParse(principal.Id, out var id) ? id : throw new Core.Errors.ForbiddenException();

    private static NotificationDto ToDto(UserNotification n) => new(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedOn, n.IsRead);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send the email for a {Kind} notification")]
    private static partial void LogEmailFailed(ILogger logger, Exception exception, string kind);
}
