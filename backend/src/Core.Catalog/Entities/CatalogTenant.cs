using System.ComponentModel.DataAnnotations.Schema;
using Acme.Core.Data;
using Acme.Core.Tenancy;

namespace Acme.Core.Catalog.Entities;

/// <summary>A tenant in the catalog. The slug is stored lowercase, so lookups never depend on database collation.</summary>
public sealed class CatalogTenant : BaseEntity, IAggregateRoot
{
    public const int MaxSlugLength = 30;

    private CatalogTenant()
    {
    }

    public CatalogTenant(string slug, string displayName, string plan, string timeZone, string currency, string locale, TenantPlacementData placement)
    {
        Slug = slug.ToLowerInvariant();
        DisplayName = displayName;
        Plan = plan;
        TimeZone = timeZone;
        Currency = currency;
        Locale = locale;
        Placement = placement;
        Status = TenantStatus.Provisioning;
    }

    public string Slug { get; private set; } = "";

    public string DisplayName { get; private set; } = "";

    public TenantStatus Status { get; private set; }

    public string Plan { get; private set; } = "";

    public string TimeZone { get; private set; } = "";

    public string Currency { get; private set; } = "";

    public string Locale { get; private set; } = "";

    public TenantPlacementData Placement { get; private set; } = new();

    /// <summary>When a tenant in <see cref="TenantStatus.PendingDeletion"/> is deleted for good (after the export and grace period).</summary>
    public DateTime? DeleteAfter { get; private set; }

    /// <summary>Whether platform operators need a tenant admin's approval for support access. On by default.</summary>
    public bool RequireSupportApproval { get; private set; } = true;

    public void ChangeStatus(TenantStatus status) => Status = status;

    public void ScheduleDeletion(DateTime? deleteAfter) => DeleteAfter = deleteAfter;

    public void RequireApprovalForSupport(bool required) => RequireSupportApproval = required;

    public void ChangePlacement(TenantPlacementData placement) => Placement = placement;

    public void ChangePlan(string plan) => Plan = plan;
}

/// <summary>Where a tenant's database lives (a complex type on <see cref="CatalogTenant"/>).</summary>
[ComplexType]
public sealed record TenantPlacementData
{
    public string Provider { get; init; } = "";

    public string ServerPool { get; init; } = "";

    public string DatabaseName { get; init; } = "";

    /// <summary>A secret-store reference (for example an AWS Secrets Manager ARN). Never a password.</summary>
    public string? SecretReference { get; init; }
}
