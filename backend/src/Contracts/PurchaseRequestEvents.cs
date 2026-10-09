namespace Acme.Contracts;

/// <summary>Events produced by the purchase-request module. One type per version; change by adding optional fields only.</summary>
public static class PurchaseRequestSubmitted
{
    /// <summary>A request was submitted and now waits for approval.</summary>
    [IntegrationEvent("purchase-request.submitted", 1)]
    public sealed record V1(
        Guid RequestId,
        string Number,
        Guid RequesterId,
        decimal Total,
        string Currency,
        DateOnly BusinessDate,
        string? CostCenter = null);
}

public static class PurchaseRequestApproved
{
    /// <summary>A request is fully approved. The requester is told; the cost-center-budget module uses the budget.</summary>
    [IntegrationEvent("purchase-request.approved", 1)]
    public sealed record V1(
        Guid RequestId,
        string Number,
        Guid RequesterId,
        Guid ApprovedBy,
        decimal Total,
        string Currency,
        DateOnly BusinessDate,
        bool AutoApproved,
        string? CostCenter = null);
}

public static class PurchaseRequestRejected
{
    /// <summary>A request was rejected. The requester is told why.</summary>
    [IntegrationEvent("purchase-request.rejected", 1)]
    public sealed record V1(
        Guid RequestId,
        string Number,
        Guid RequesterId,
        Guid RejectedBy,
        string Reason);
}

public static class ApprovalReminderDue
{
    /// <summary>
    /// The Scheduler's daily trigger for one tenant (it is 09:00 there). The purchase-request module answers with
    /// <see cref="ApprovalsWaiting.V1"/> if requests have waited too long. One message per tenant, handled in parallel.
    /// </summary>
    [IntegrationEvent("scheduler.approval-reminder-due", 1)]
    public sealed record V1(DateOnly BusinessDate);
}

public static class ApprovalsWaiting
{
    /// <summary>Requests have waited more than two days for approval. Notification tells the approvers.</summary>
    [IntegrationEvent("purchase-request.approvals-waiting", 1)]
    public sealed record V1(int Count, DateOnly OldestSubmittedOn, IReadOnlyList<string> Numbers);
}
