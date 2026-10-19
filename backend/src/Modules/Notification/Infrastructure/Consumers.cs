using Saaskit.Contracts;
using Saaskit.Core.Messaging;
using Saaskit.Core.Platform;
using Saaskit.Notification.Application;

namespace Saaskit.Notification.Infrastructure;

/// <summary>Delivers a notification another module asked for, to one person or to the members of a module.</summary>
internal sealed class NotificationRequestedConsumer(NotificationService notifications, IContactDirectory contacts) : IEventConsumer<NotificationRequested.V1>
{
    public async Task HandleAsync(NotificationRequested.V1 integrationEvent, CancellationToken cancellationToken)
    {
        IReadOnlyList<Contact> recipients;
        if (integrationEvent.RecipientId is { } id)
            recipients = await contacts.FindAsync(id, cancellationToken) is { } contact ? [contact] : [];   // gone, or can't sign in
        else if (integrationEvent.RecipientModule is { } module)
            recipients = await contacts.MembersOfAsync(module, integrationEvent.RecipientMinimumRole ?? Roles.User, cancellationToken);
        else
            recipients = [];

        foreach (var recipient in recipients)
        {
            await notifications.DeliverAsync(recipient, integrationEvent.Kind, integrationEvent.Title, integrationEvent.Body,
                integrationEvent.EmailSubject ?? integrationEvent.Title, integrationEvent.Link);
        }
    }
}
