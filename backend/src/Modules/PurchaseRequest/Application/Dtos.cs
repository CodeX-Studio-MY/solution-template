using Acme.PurchaseRequest.Domain;

namespace Acme.PurchaseRequest.Application;

public sealed record LineInput(string Item, decimal Quantity, decimal UnitPrice);

public sealed record SaveRequestInput(string Title, string? Description, IReadOnlyList<LineInput> Lines);

public sealed record RejectInput(string Reason);

public sealed record LineDto(string Item, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>What a screen needs to show a request, including the actions this caller may take. The frontend shows buttons from <see cref="AllowedActions"/> and never re-implements the rules.</summary>
public sealed record RequestDto(
    Guid Id,
    string Number,
    Guid RequesterId,
    string Title,
    string? Description,
    RequestStatus Status,
    DateOnly BusinessDate,
    string Currency,
    decimal Total,
    IReadOnlyList<LineDto> Lines,
    Guid? QuoteFileId,
    DateTime CreatedOn,
    DateTime? SubmittedOn,
    DateTime? DecidedOn,
    bool AutoApproved,
    string? RejectionReason,
    DateTime? GoodsReceivedOn,
    IReadOnlyList<string> AllowedActions,
    string ETag);

/// <summary>A row in a list: lighter than the full request.</summary>
public sealed record RequestSummaryDto(Guid Id, string Number, string Title, RequestStatus Status, decimal Total, string Currency, DateOnly BusinessDate, Guid RequesterId);

/// <summary>The actions a caller may take on a request. Names are part of the API contract.</summary>
public static class RequestActions
{
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string UploadQuote = "uploadQuote";
}
