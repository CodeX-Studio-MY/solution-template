using Acme.Core.Platform;

namespace Acme.PurchaseRequest.Domain;

/// <summary>The next approval a request needs: which level this is, and the lowest role that may give it.</summary>
public sealed record ApprovalStep(int Level, Role MinimumRole);

/// <summary>
/// How a tenant approves requests. The logic differs per tenant, so it is a strategy chosen by the tenant's settings (rung 3 of the
/// customization ladder), never an <c>if</c> on the tenant. The default is <see cref="SingleApproverPolicy"/>.
/// </summary>
public interface IApprovalPolicy
{
    /// <summary>The next approval needed, or null when the request is fully approved.</summary>
    ApprovalStep? NextStep(PurchaseRequest request);
}

/// <summary>Any one manager approves.</summary>
public sealed class SingleApproverPolicy : IApprovalPolicy
{
    public const string Key = "single";

    public ApprovalStep? NextStep(PurchaseRequest request) =>
        request.Approvals.Count == 0 ? new ApprovalStep(1, Role.Manager) : null;
}
