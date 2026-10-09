namespace Acme.Core.Tenancy;

/// <summary>Tenant lifecycle: Provisioning → Active → Suspended (read-only) → PendingDeletion → Deleted.</summary>
public enum TenantStatus
{
    Provisioning = 0,
    Active = 1,
    Suspended = 2,
    PendingDeletion = 3,
    Deleted = 4,
}

/// <summary>Where a tenant's database lives. Placement is data in the catalog, not code.</summary>
/// <param name="Provider">The database provider, for example <c>sqlserver</c>.</param>
/// <param name="ServerPool">The server or pool name; its base connection string comes from configuration.</param>
/// <param name="DatabaseName">The tenant's database on that server.</param>
/// <param name="SecretReference">Optional secret-store reference (for example an AWS Secrets Manager ARN) for the credentials.</param>
public sealed record TenantPlacement(string Provider, string ServerPool, string DatabaseName, string? SecretReference);

/// <summary>Rate limits from the tenant's plan, when the plan sets its own.</summary>
public sealed record PlanLimits(int PerTenantPerMinute, int PerPrincipalPerMinute);

/// <summary>A tenant as the catalog describes it.</summary>
public sealed record TenantInfo(
    Guid Id,
    string Slug,
    string DisplayName,
    TenantStatus Status,
    string Plan,
    string TimeZone,
    string Currency,
    string Locale,
    IReadOnlySet<string> EnabledModules,
    TenantPlacement Placement,
    PlanLimits? Limits = null,
    bool RequireSupportApproval = true,
    DateTime? DeleteAfter = null)
{
    /// <summary>Suspended tenants can sign in and read but not write.</summary>
    public bool CanRead => Status is TenantStatus.Active or TenantStatus.Suspended;

    public bool CanWrite => Status is TenantStatus.Active;
}
