using Farm360.Core.Platform;
using Farm360.Core.Tenancy;

namespace Farm360.Tenancy.Application;

public sealed record TenantSummary(
    Guid Id,
    string Slug,
    string DisplayName,
    TenantStatus Status,
    string Plan,
    IReadOnlyList<string> Modules,
    DateTime? DeleteAfter,
    bool RequireSupportApproval);

public sealed record OnboardRequest(string Slug, string DisplayName, string Plan, string AdminEmail, string AdminName, string? ServerPool = null);

/// <summary>The outcome of onboarding. <paramref name="AdminInvitationUrl"/> is the first admin's one-time link; null when onboarding resumed after it was already sent.</summary>
public sealed record OnboardResult(Guid TenantId, string Slug, Uri? AdminInvitationUrl);

/// <summary>Reads and changes tenants: plan, add-on modules, support-access policy.</summary>
public interface ITenantAdmin
{
    Task<IReadOnlyList<TenantSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<TenantSummary> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Changes the plan and reconciles the tenant's modules with it. Takes effect everywhere within seconds.</summary>
    Task<TenantSummary> ChangePlanAsync(Guid tenantId, string plan, string actor, CancellationToken cancellationToken = default);

    /// <summary>Grants a module on top of the plan (an add-on). Entitlement is checked live, so this is immediate.</summary>
    Task<TenantSummary> GrantModuleAsync(Guid tenantId, string moduleKey, string actor, CancellationToken cancellationToken = default);

    Task<TenantSummary> RevokeModuleAsync(Guid tenantId, string moduleKey, string actor, CancellationToken cancellationToken = default);

    Task<TenantSummary> SetSupportApprovalAsync(Guid tenantId, bool required, string actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provisioning → Active → Suspended (read-only) → PendingDeletion → Deleted. Each move is audited and published, so
/// every instance picks up the new status within seconds.
/// </summary>
public interface ITenantLifecycle
{
    Task<TenantSummary> SuspendAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default);

    Task<TenantSummary> ResumeAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts offboarding: revokes tokens, produces the full data export, removes the tenant's callback URIs and schedules
    /// deletion after the grace period. The tenant becomes read-only until then.
    /// </summary>
    Task<TenantSummary> BeginOffboardingAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Cancels offboarding during the grace period.</summary>
    Task<TenantSummary> CancelOffboardingAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Deletes the tenant's database and marks it Deleted. Only after the grace period has passed.</summary>
    Task<TenantSummary> CompleteDeletionAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default);
}

/// <summary>Idempotent, resumable tenant onboarding. Running it again for the same slug continues where it stopped.</summary>
public interface IOnboarding
{
    Task<OnboardResult> OnboardAsync(OnboardRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Applies one tenant's migrations. Tenancy never applies migrations itself: the pipeline's Migrator does.</summary>
public interface ITenantMigrator
{
    Task MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Produces a tenant's full, machine-readable data export (per module) before offboarding deletes anything.</summary>
public interface ITenantExporter
{
    Task ExportAsync(TenantInfo tenant, CancellationToken cancellationToken = default);
}

/// <summary>Billing is an interface only (out of scope): wire a payment provider here.</summary>
public interface IBillingProvider
{
    Task<BillingStatus> GetStatusAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed record BillingStatus(bool InGoodStanding, string? Note);

/// <summary>Calls to the Identity module's platform endpoints, with this service's token.</summary>
public interface IIdentityClient
{
    Task<Uri?> InviteFirstAdminAsync(Guid tenantId, string email, string displayName, IEnumerable<string> modules, CancellationToken cancellationToken = default);

    Task SetBffRedirectUrisAsync(Guid tenantId, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken = default);

    Task RevokeAllTokensAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<SupportTokenDto> IssueSupportTokenAsync(Guid tenantId, SupportTokenCall request, CancellationToken cancellationToken = default);
}

public sealed record SupportTokenCall(Guid GrantId, string OperatorId, string OperatorName, string Module, Role Role, string Audience, string Reason);

public sealed record SupportTokenDto(string AccessToken, DateTime ExpiresOn);

/// <summary>The frontend module manifest for the signed-in tenant: which remotes the shell registers.</summary>
public sealed record ModuleManifestEntry(string Id, string Entry);

public sealed record ModuleManifest(IReadOnlyList<ModuleManifestEntry> Modules);

public interface IModuleManifestProvider
{
    Task<ModuleManifest> ForCurrentTenantAsync(CancellationToken cancellationToken = default);
}
