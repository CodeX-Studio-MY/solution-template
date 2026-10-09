using Acme.Contracts;
using Acme.Core.Messaging;
using Acme.Core.Time;
using Acme.PurchaseRequest.Domain;
using Microsoft.EntityFrameworkCore;

namespace Acme.PurchaseRequest.Infrastructure;

/// <summary>
/// The Scheduler says "it is 09:00 for this tenant". If requests have waited more than two days for approval, this publishes
/// one <see cref="ApprovalsWaiting.V1"/> for Notification to tell the approvers.
/// </summary>
internal sealed class ApprovalReminderConsumer(PurchaseRequestDbContext db, IEventPublisher events, ITenantClock clock, TimeProvider time)
    : IEventConsumer<ApprovalReminderDue.V1>
{
    public static readonly TimeSpan WaitingLimit = TimeSpan.FromDays(2);
    private const int MaxListed = 20;

    public async Task HandleAsync(ApprovalReminderDue.V1 integrationEvent, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - WaitingLimit;
        var waiting = db.Requests.Where(r => r.Status == RequestStatus.Submitted && r.SubmittedOn < cutoff);
        var count = await waiting.CountAsync(cancellationToken);
        if (count == 0)
            return;

        var oldest = await waiting.OrderBy(r => r.SubmittedOn).Select(r => new { r.Number, r.SubmittedOn }).Take(MaxListed).ToListAsync(cancellationToken);
        var oldestDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(oldest[0].SubmittedOn!.Value, clock.TimeZone));
        events.Publish(db, new ApprovalsWaiting.V1(count, oldestDate, [.. oldest.Select(r => r.Number)]));
    }
}
