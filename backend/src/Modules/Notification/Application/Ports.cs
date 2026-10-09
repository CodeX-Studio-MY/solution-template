using Acme.Core.Http;
using Acme.Notification.Domain;

namespace Acme.Notification.Application;

public sealed record NotificationDto(Guid Id, string Kind, string Title, string Body, string? Link, DateTime CreatedOn, bool IsRead);

public sealed record Contact(Guid Id, string DisplayName, string? Email);

/// <summary>Where people are reached: the Identity module knows names and addresses; Notification asks, it doesn't store them.</summary>
public interface IContactDirectory
{
    Task<Contact?> FindAsync(Guid principalId, CancellationToken cancellationToken = default);

    /// <summary>Everyone who can approve in a module (at least manager there).</summary>
    Task<IReadOnlyList<Contact>> ApproversOfAsync(string moduleKey, CancellationToken cancellationToken = default);
}

/// <summary>Sends email. The default adapter speaks SMTP (Mailpit locally). Templates are localized per tenant by the caller.</summary>
public interface IEmailSender
{
    Task SendAsync(string toAddress, string toName, string subject, string textBody, CancellationToken cancellationToken = default);
}

/// <summary>Pushes a new notification to the person's open browser sessions in real time (SignalR).</summary>
public interface INotificationPusher
{
    Task PushAsync(Guid tenantId, Guid recipientId, NotificationDto notification, CancellationToken cancellationToken = default);
}

public interface INotificationStore
{
    UserNotification Add(UserNotification notification);

    Task<Page<UserNotification>> ListAsync(Guid recipientId, bool unreadOnly, PageRequest page, CancellationToken cancellationToken = default);

    Task<int> UnreadCountAsync(Guid recipientId, CancellationToken cancellationToken = default);

    Task<UserNotification?> FindAsync(Guid id, Guid recipientId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(Guid recipientId, DateTime utcNow, CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
