using System.Text.Json;
using Acme.Core.Data;

namespace Acme.PurchaseRequest.Domain;

public enum RequestStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>A line on a request: an item, how many, and the price of one.</summary>
public sealed class RequestLine
{
    public const int MaxItemLength = 200;

    private RequestLine()
    {
    }

    public RequestLine(string item, decimal quantity, decimal unitPrice)
    {
        Item = item.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string Item { get; private set; } = "";

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 4, MidpointRounding.AwayFromZero);
}

/// <summary>One approval given to a request, at a level of the tenant's approval policy.</summary>
public sealed class RequestApproval
{
    private RequestApproval()
    {
    }

    public RequestApproval(int level, Guid approverId, DateTime approvedOn)
    {
        Level = level;
        ApproverId = approverId;
        ApprovedOn = approvedOn;
    }

    public int Level { get; private set; }

    public Guid ApproverId { get; private set; }

    public DateTime ApprovedOn { get; private set; }
}

/// <summary>
/// A purchase request: staff add lines and a supplier quote, submit it, and a manager approves or rejects it. All state changes go
/// through this class, so the rules (a draft is editable, a submitted request is not, nobody approves their own request) live in one place.
/// </summary>
public sealed class PurchaseRequest : BaseEntity, IAggregateRoot, ISoftDeletable
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxLines = 200;

    private readonly List<RequestLine> _lines = [];
    private readonly List<RequestApproval> _approvals = [];

    private PurchaseRequest()
    {
    }

    public PurchaseRequest(string number, Guid requesterId, string title, string? description, DateOnly businessDate, string currency)
    {
        Number = number;
        RequesterId = requesterId;
        Title = title.Trim();
        Description = description?.Trim();
        BusinessDate = businessDate;
        Currency = currency;
        Status = RequestStatus.Draft;
    }

    public string Number { get; private set; } = "";

    public Guid RequesterId { get; private set; }

    public string Title { get; private set; } = "";

    public string? Description { get; private set; }

    public RequestStatus Status { get; private set; }

    /// <summary>The business day the request was made, in the tenant's time zone.</summary>
    public DateOnly BusinessDate { get; private set; }

    public string Currency { get; private set; } = "";

    /// <summary>The sum of the lines. Kept as a column so lists can filter and sort on it.</summary>
    public decimal TotalAmount { get; private set; }

    public Money Total => new(TotalAmount, Currency);

    public IReadOnlyList<RequestLine> Lines => _lines;

    public IReadOnlyList<RequestApproval> Approvals => _approvals;

    /// <summary>The tenant's extra fields (rung 4) as a JSON object of key → text, kept in one native JSON column.</summary>
    public string CustomFieldsJson { get; private set; } = "{}";

    public IReadOnlyDictionary<string, string> CustomFields =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(CustomFieldsJson) ?? [];

    public Guid? QuoteFileId { get; private set; }

    public DateTime? SubmittedOn { get; private set; }

    public DateTime? DecidedOn { get; private set; }

    public bool AutoApproved { get; private set; }

    public string? RejectionReason { get; private set; }

    public Guid? RejectedBy { get; private set; }

    public DateTime? GoodsReceivedOn { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedOn { get; private set; }

    public bool IsEditable => Status == RequestStatus.Draft;

    public void ReplaceCustomFields(IReadOnlyDictionary<string, string> values)
    {
        EnsureDraft();
        CustomFieldsJson = JsonSerializer.Serialize(values.OrderBy(v => v.Key, StringComparer.Ordinal).ToDictionary(v => v.Key, v => v.Value));
    }

    public void ReplaceDetails(string title, string? description, IEnumerable<RequestLine> lines)
    {
        EnsureDraft();
        Title = title.Trim();
        Description = description?.Trim();
        _lines.Clear();
        _lines.AddRange(lines);
        TotalAmount = _lines.Sum(l => l.LineTotal);
    }

    public void AttachQuote(Guid fileId)
    {
        EnsureDraft();
        QuoteFileId = fileId;
    }

    public void Submit(DateTime utcNow)
    {
        EnsureDraft();
        if (_lines.Count == 0)
            throw new RequestStateException(RequestStateError.NoLines);
        Status = RequestStatus.Submitted;
        SubmittedOn = utcNow;
    }

    /// <summary>Approved without a human: the total is below the tenant's auto-approve threshold.</summary>
    public void AutoApprove(DateTime utcNow)
    {
        EnsureSubmitted();
        Status = RequestStatus.Approved;
        AutoApproved = true;
        DecidedOn = utcNow;
    }

    /// <summary>
    /// Records one approval, then asks the tenant's policy whether another is needed. The request is fully approved when
    /// <paramref name="nextStep"/> returns null.
    /// </summary>
    public void Approve(int level, Guid approverId, DateTime utcNow, Func<PurchaseRequest, ApprovalStep?> nextStep)
    {
        EnsureSubmitted();
        if (approverId == RequesterId)
            throw new RequestStateException(RequestStateError.OwnRequest);
        if (_approvals.Any(a => a.ApproverId == approverId))
            throw new RequestStateException(RequestStateError.AlreadyApprovedByYou);
        _approvals.Add(new RequestApproval(level, approverId, utcNow));
        if (nextStep(this) is null)
        {
            Status = RequestStatus.Approved;
            DecidedOn = utcNow;
        }
    }

    public void Reject(Guid rejectedBy, string reason, DateTime utcNow)
    {
        EnsureSubmitted();
        if (rejectedBy == RequesterId)
            throw new RequestStateException(RequestStateError.OwnRequest);
        Status = RequestStatus.Rejected;
        RejectedBy = rejectedBy;
        RejectionReason = reason.Trim();
        DecidedOn = utcNow;
    }

    /// <summary>A machine (for example a scanner at the loading dock) confirms the goods arrived.</summary>
    public void ReceiveGoods(DateTime utcNow)
    {
        if (Status != RequestStatus.Approved)
            throw new RequestStateException(RequestStateError.NotApproved);
        GoodsReceivedOn ??= utcNow;
    }

    public void MarkDeleted(DateTime utcNow)
    {
        EnsureDraft();
        IsDeleted = true;
        DeletedOn = utcNow;
    }

    private void EnsureDraft()
    {
        if (Status != RequestStatus.Draft)
            throw new RequestStateException(RequestStateError.NotDraft);
    }

    private void EnsureSubmitted()
    {
        if (Status != RequestStatus.Submitted)
            throw new RequestStateException(RequestStateError.NotSubmitted);
    }
}

public enum RequestStateError
{
    NotDraft,
    NotSubmitted,
    NotApproved,
    NoLines,
    OwnRequest,
    AlreadyApprovedByYou,
}

/// <summary>A rule of the request's life cycle was broken. The Application layer turns it into the module's error code.</summary>
public sealed class RequestStateException(RequestStateError error) : Exception($"Request state rule broken: {error}")
{
    public RequestStateError Error { get; } = error;
}
