using Acme.Core.Errors;
using Acme.Core.Platform;
using Acme.Core.Authorization;

namespace Acme.PurchaseRequest.Application;

/// <summary>The purchase-request module's error catalog. Frontend error-code types are generated from it.</summary>
[ErrorCatalog(Modules.PurchaseRequest)]
public static class PurchaseRequestErrors
{
    public static readonly ErrorCode NotFound = new("purchase-request.not-found", 404, "errors.purchaseRequest.notFound");
    public static readonly ErrorCode NotDraft = new("purchase-request.not-draft", 422, "errors.purchaseRequest.notDraft");
    public static readonly ErrorCode NotSubmitted = new("purchase-request.not-submitted", 422, "errors.purchaseRequest.notSubmitted");
    public static readonly ErrorCode NotApproved = new("purchase-request.not-approved", 422, "errors.purchaseRequest.notApproved");
    public static readonly ErrorCode NoLines = new("purchase-request.no-lines", 422, "errors.purchaseRequest.noLines");
    public static readonly ErrorCode OwnRequest = new("purchase-request.own-request", 403, "errors.purchaseRequest.ownRequest");
    public static readonly ErrorCode AlreadyApprovedByYou = new("purchase-request.already-approved-by-you", 422, "errors.purchaseRequest.alreadyApprovedByYou");
    public static readonly ErrorCode NotYours = new("purchase-request.not-yours", 403, "errors.purchaseRequest.notYours");
    public static readonly ErrorCode RoleTooLow = new("purchase-request.role-too-low", 403, "errors.purchaseRequest.roleTooLow");
    public static readonly ErrorCode NoQuote = new("purchase-request.no-quote", 404, "errors.purchaseRequest.noQuote");
}

/// <summary>Finer rules, as permission → lowest role. They never go in the token (4.5).</summary>
public static class PurchaseRequestPermissions
{
    /// <summary>Approving needs a manager (the tenant's approval policy may ask for more at a later step).</summary>
    public static readonly ModulePermission Approve = new(Modules.PurchaseRequest, "approve", Role.Manager);
}
