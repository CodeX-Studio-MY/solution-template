using Saaskit.Core.Http;
using Saaskit.Notification.Application;
using Saaskit.Notification.Domain;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Notification.Infrastructure;

internal sealed class NotificationStore(NotificationDbContext db) : INotificationStore
{
    public UserNotification Add(UserNotification notification) => db.Notifications.Add(notification).Entity;

    public async Task<Page<UserNotification>> ListAsync(Guid recipientId, bool unreadOnly, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientId == recipientId);
        if (unreadOnly)
            query = query.Where(n => n.ReadOn == null);
        return await query.OrderByDescending(n => n.CreatedOn).ThenBy(n => n.Id).ToPageAsync(page, cancellationToken);
    }

    public Task<int> UnreadCountAsync(Guid recipientId, CancellationToken cancellationToken = default) =>
        db.Notifications.CountAsync(n => n.RecipientId == recipientId && n.ReadOn == null, cancellationToken);

    public Task<UserNotification?> FindAsync(Guid id, Guid recipientId, CancellationToken cancellationToken = default) =>
        db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.RecipientId == recipientId, cancellationToken);

    public Task<int> MarkAllReadAsync(Guid recipientId, DateTime utcNow, CancellationToken cancellationToken = default) =>
        db.Notifications.Where(n => n.RecipientId == recipientId && n.ReadOn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadOn, utcNow), cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
