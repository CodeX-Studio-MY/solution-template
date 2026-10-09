using System.ComponentModel.DataAnnotations;

namespace Acme.Contracts;

/// <summary>The approval strategies a tenant can choose (rung 3). A key is written here once; the module registers one strategy per key.</summary>
public static class ApprovalPolicyKeys
{
    /// <summary>Any one manager approves.</summary>
    public const string SingleApprover = "single";

    /// <summary>A manager approves, then an admin as well above the second-approval threshold.</summary>
    public const string TwoLevel = "two-level";
}

/// <summary>The tenant's choices for the purchase-request module. Every value has a default.</summary>
[TenantSettings(ModuleKeys.PurchaseRequest)]
public sealed record PurchaseRequestSettings
{
    /// <summary>Requests below this total are approved on submit.</summary>
    [TenantEditable, Range(0, 1_000_000_000)]
    public decimal AutoApproveBelow { get; init; } = 500m;

    /// <summary>Which approval strategy the tenant uses.</summary>
    [AllowedValues(ApprovalPolicyKeys.SingleApprover, ApprovalPolicyKeys.TwoLevel)]
    public string ApprovalPolicy { get; init; } = ApprovalPolicyKeys.SingleApprover;

    /// <summary>With the two-level policy, requests above this total also need an admin's approval.</summary>
    [TenantEditable, Range(0, 1_000_000_000)]
    public decimal SecondApprovalAbove { get; init; } = 5000m;

    /// <summary>How request numbers look. <c>{date:format}</c> is the business date, <c>{seq:format}</c> the day's counter.</summary>
    [StringLength(60, MinimumLength = 1)]
    public string NumberFormat { get; init; } = "PR-{date:yyyyMMdd}-{seq:0000}";
}

/// <summary>The purchase-request module's feature flags (rung 2).</summary>
public static class PurchaseRequestFeatures
{
    /// <summary>Lets the owner of a draft hand its link to someone else. Off by default: behaves as if the endpoint doesn't exist.</summary>
    [FeatureFlag(ModuleKeys.PurchaseRequest, DefaultEnabled = false, Description = "Share a draft request with a link.")]
    public const string DraftSharing = "purchase-request.draft-sharing";
}

/// <summary>Custom field keys that modules agree on, so one module's data can feed another through events (rung 4 and 5).</summary>
public static class WellKnownCustomFields
{
    /// <summary>The cost center a purchase request is charged to. The budget module uses it when a request is approved.</summary>
    public const string CostCenter = "cost-center";
}
