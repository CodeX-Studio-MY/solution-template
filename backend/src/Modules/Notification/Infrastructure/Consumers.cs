using System.Globalization;
using Acme.Contracts;
using Acme.Core.Messaging;
using Acme.Core.Platform;
using Acme.Notification.Application;

namespace Acme.Notification.Infrastructure;

/// <summary>Tells the requester their request was approved (in the app and by email).</summary>
internal sealed class PurchaseRequestApprovedConsumer(NotificationService notifications, IContactDirectory contacts) : IEventConsumer<PurchaseRequestApproved.V1>
{
    public async Task HandleAsync(PurchaseRequestApproved.V1 integrationEvent, CancellationToken cancellationToken)
    {
        var requester = await contacts.FindAsync(integrationEvent.RequesterId, cancellationToken);
        if (requester is null)
            return;   // the person no longer exists or can't sign in
        await notifications.NotifyAsync(
            requester,
            "purchase-request.approved",
            "notifications.approved",
            $"/{Modules.PurchaseRequest}/{integrationEvent.RequestId}",
            integrationEvent.Number,
            integrationEvent.Currency,
            integrationEvent.Total.ToString("N2", CultureInfo.InvariantCulture));
    }
}

/// <summary>Tells the requester their request was rejected, and why.</summary>
internal sealed class PurchaseRequestRejectedConsumer(NotificationService notifications, IContactDirectory contacts) : IEventConsumer<PurchaseRequestRejected.V1>
{
    public async Task HandleAsync(PurchaseRequestRejected.V1 integrationEvent, CancellationToken cancellationToken)
    {
        var requester = await contacts.FindAsync(integrationEvent.RequesterId, cancellationToken);
        if (requester is null)
            return;
        await notifications.NotifyAsync(
            requester,
            "purchase-request.rejected",
            "notifications.rejected",
            $"/{Modules.PurchaseRequest}/{integrationEvent.RequestId}",
            integrationEvent.Number,
            integrationEvent.Reason);
    }
}

/// <summary>The daily reminder: tells everyone who can approve that requests have been waiting more than two days.</summary>
internal sealed class ApprovalsWaitingConsumer(NotificationService notifications, IContactDirectory contacts) : IEventConsumer<ApprovalsWaiting.V1>
{
    public async Task HandleAsync(ApprovalsWaiting.V1 integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var approver in await contacts.ApproversOfAsync(Modules.PurchaseRequest, cancellationToken))
        {
            await notifications.NotifyAsync(
                approver,
                "purchase-request.approvals-waiting",
                "notifications.approvalsWaiting",
                $"/{Modules.PurchaseRequest}",
                integrationEvent.Count.ToString(CultureInfo.InvariantCulture),
                integrationEvent.OldestSubmittedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                string.Join(", ", integrationEvent.Numbers));
        }
    }
}
