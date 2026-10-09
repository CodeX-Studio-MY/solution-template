using Acme.Core.Platform;

namespace Acme.PurchaseRequest.Domain;

/// <summary>The next approval a request needs: which level this is, and the lowest role that may give it.</summary>
public sealed record ApprovalStep(int Level, Role MinimumRole);

/// <summary>The tenant's numbers a policy may need (from the tenant's settings).</summary>
public sealed record ApprovalRules(decimal SecondApprovalAbove);

/// <summary>
/// How a tenant approves requests. The logic differs per tenant, so it is a strategy chosen by the tenant's settings (rung 3 of the
/// customization ladder), never an <c>if</c> on the tenant. The default is <see cref="SingleApproverPolicy"/>.
/// </summary>
public interface IApprovalPolicy
{
    /// <summary>The next approval needed, or null when the request is fully approved.</summary>
    ApprovalStep? NextStep(PurchaseRequest request, ApprovalRules rules);
}

/// <summary>Any one manager approves.</summary>
public sealed class SingleApproverPolicy : IApprovalPolicy
{
    public ApprovalStep? NextStep(PurchaseRequest request, ApprovalRules rules) =>
        request.Approvals.Count == 0 ? new ApprovalStep(1, Role.Manager) : null;
}

/// <summary>A manager approves; above the tenant's threshold an admin has to approve as well (a different person).</summary>
public sealed class TwoLevelApprovalPolicy : IApprovalPolicy
{
    public ApprovalStep? NextStep(PurchaseRequest request, ApprovalRules rules) => request.Approvals.Count switch
    {
        0 => new ApprovalStep(1, Role.Manager),
        1 when request.TotalAmount > rules.SecondApprovalAbove => new ApprovalStep(2, Role.Admin),
        _ => null,
    };
}
