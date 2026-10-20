using System.Text.Json;
using Farm360.Core.Catalog;
using Farm360.Core.Catalog.Entities;
using Farm360.Core.Configuration;
using Farm360.Core.Data;
using Farm360.Core.Errors;
using Farm360.Core.Platform;
using Farm360.Core.Tenancy;
using Farm360.Tenancy.Application;
using Farm360.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Farm360.Tenancy.Infrastructure;

/// <summary>Plan, entitlements, support-access policy and the tenant lifecycle.</summary>
internal sealed class TenantManagement(
    CatalogDbContext catalog,
    TenancyCatalogDbContext tenancy,
    ITenantChangeNotifier changes,
    IIdentityClient identity,
    ITenantExporter exporter,
    ITenantDirectory directory,
    IDatabaseProviderAccessor providers,
    IOptions<DatabaseOptions> database,
    IOptions<OnboardingOptions> onboarding,
    IOptions<TenancyOptions> tenancyOptions,
    TimeProvider time) : ITenantAdmin, ITenantLifecycle
{
    // ---- Admin ----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TenantSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await catalog.Tenants.AsNoTracking().OrderBy(t => t.Slug).ToListAsync(cancellationToken);
        var modules = await ModulesByTenantAsync(tenants.Select(t => t.Id).ToList(), cancellationToken);
        return [.. tenants.Select(t => ToSummary(t, modules[t.Id]))];
    }

    public async Task<TenantSummary> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await FindAsync(tenantId, cancellationToken);
        return ToSummary(tenant, (await ModulesByTenantAsync([tenantId], cancellationToken))[tenantId]);
    }

    public async Task<TenantSummary> ChangePlanAsync(Guid tenantId, string plan, string actor, CancellationToken cancellationToken = default)
    {
        if (!Plans.All.Contains(plan))
            throw new BusinessRuleException(TenancyErrors.UnknownPlan, plan);
        var tenant = await FindAsync(tenantId, cancellationToken);
        var previous = tenant.Plan;
        tenant.ChangePlan(plan);

        // Reconcile: the plan's modules are entitled; modules from the previous plan that the new plan lacks are removed.
        // Add-ons granted on top of a plan are kept only if they were not part of the previous plan.
        var oldPlanModules = Modules.All.Where(m => m.Plans.Contains(previous)).Select(m => m.Id).ToHashSet();
        var newPlanModules = Modules.All.Where(m => m.Plans.Contains(plan)).Select(m => m.Id).ToHashSet();
        var current = await catalog.TenantModules.Where(t => t.TenantId == tenantId).ToListAsync(cancellationToken);
        foreach (var row in current.Where(r => oldPlanModules.Contains(r.ModuleId) && !newPlanModules.Contains(r.ModuleId)))
            catalog.TenantModules.Remove(row);
        foreach (var moduleId in newPlanModules.Where(id => current.All(r => r.ModuleId != id)))
            catalog.TenantModules.Add(new TenantModule { TenantId = tenantId, ModuleId = moduleId });

        await AuditAsync(tenantId, "plan-changed", actor, new { from = previous, to = plan });
        await SaveAndPublishAsync(tenantId, cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    public async Task<TenantSummary> GrantModuleAsync(Guid tenantId, string moduleKey, string actor, CancellationToken cancellationToken = default)
    {
        await FindAsync(tenantId, cancellationToken);
        var moduleId = ModuleId(moduleKey);
        if (!await catalog.TenantModules.AnyAsync(t => t.TenantId == tenantId && t.ModuleId == moduleId, cancellationToken))
            catalog.TenantModules.Add(new TenantModule { TenantId = tenantId, ModuleId = moduleId });
        await AuditAsync(tenantId, "module-granted", actor, new { module = moduleKey });
        await SaveAndPublishAsync(tenantId, cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    public async Task<TenantSummary> RevokeModuleAsync(Guid tenantId, string moduleKey, string actor, CancellationToken cancellationToken = default)
    {
        await FindAsync(tenantId, cancellationToken);
        var moduleId = ModuleId(moduleKey);
        var row = await catalog.TenantModules.SingleOrDefaultAsync(t => t.TenantId == tenantId && t.ModuleId == moduleId, cancellationToken);
        if (row is not null)
            catalog.TenantModules.Remove(row);
        await AuditAsync(tenantId, "module-revoked", actor, new { module = moduleKey });
        await SaveAndPublishAsync(tenantId, cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    public async Task<TenantSummary> SetSupportApprovalAsync(Guid tenantId, bool required, string actor, CancellationToken cancellationToken = default)
    {
        var tenant = await FindAsync(tenantId, cancellationToken);
        tenant.RequireApprovalForSupport(required);
        await AuditAsync(tenantId, "support-approval-policy", actor, new { required });
        await SaveAndPublishAsync(tenantId, cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    // ---- Lifecycle ------------------------------------------------------------------------------

    public Task<TenantSummary> SuspendAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default) =>
        MoveAsync(tenantId, TenantStatus.Suspended, actor, cancellationToken);

    public Task<TenantSummary> ResumeAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default) =>
        MoveAsync(tenantId, TenantStatus.Active, actor, cancellationToken);

    public async Task<TenantSummary> BeginOffboardingAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default)
    {
        var tenant = await FindAsync(tenantId, cancellationToken);
        Require(tenant, TenantStatus.PendingDeletion);

        // Order matters. While the tenant can still be read: sign everyone out, produce the full export, remove its callbacks.
        // Only then does it become PendingDeletion (after which services refuse it).
        var info = await directory.FindByIdAsync(tenantId, cancellationToken) ?? throw new NotFoundException(TenancyErrors.TenantNotFound);
        await identity.RevokeAllTokensAsync(tenantId, cancellationToken);
        await exporter.ExportAsync(info, cancellationToken);
        await identity.SetBffRedirectUrisAsync(tenantId, [], CallbackUris(tenant.Slug), cancellationToken);

        tenant.ChangeStatus(TenantStatus.PendingDeletion);
        tenant.ScheduleDeletion(time.GetUtcNow().UtcDateTime.AddDays(onboarding.Value.DeletionGraceDays));
        await AuditAsync(tenantId, "offboarding-begun", actor, new { deleteAfter = tenant.DeleteAfter });
        await SaveAndPublishAsync(tenantId, cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    public async Task<TenantSummary> CancelOffboardingAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default)
    {
        var tenant = await FindAsync(tenantId, cancellationToken);
        if (tenant.Status != TenantStatus.PendingDeletion)
            throw new BusinessRuleException(TenancyErrors.InvalidTransition, "Only a tenant pending deletion can be restored.");
        tenant.ScheduleDeletion(null);
        tenant.ChangeStatus(TenantStatus.Active);
        await AuditAsync(tenantId, "offboarding-cancelled", actor, null);
        await SaveAndPublishAsync(tenantId, cancellationToken);
        // The tenant is Active again, so Identity accepts the callbacks.
        await identity.SetBffRedirectUrisAsync(tenantId, CallbackUris(tenant.Slug), [], cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    public async Task<TenantSummary> CompleteDeletionAsync(Guid tenantId, string actor, CancellationToken cancellationToken = default)
    {
        var tenant = await FindAsync(tenantId, cancellationToken);
        Require(tenant, TenantStatus.Deleted);
        if (tenant.DeleteAfter is null || time.GetUtcNow().UtcDateTime < tenant.DeleteAfter)
            throw new BusinessRuleException(TenancyErrors.InvalidTransition, "The grace period has not passed.");

        var info = await directory.FindByIdAsync(tenantId, cancellationToken) ?? throw new NotFoundException(TenancyErrors.TenantNotFound);
        var provider = providers.ForName(info.Placement.Provider);
        var pool = database.Value.Servers[info.Placement.ServerPool];
        await provider.DropDatabaseAsync(pool, info.Placement.DatabaseName, cancellationToken);   // the tenant's file prefix is deleted with its module data (M3)

        tenant.ChangeStatus(TenantStatus.Deleted);
        catalog.TenantModules.RemoveRange(catalog.TenantModules.Where(t => t.TenantId == tenantId));
        await AuditAsync(tenantId, "tenant-deleted", actor, null);
        await SaveAndPublishAsync(tenantId, cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    private async Task<TenantSummary> MoveAsync(Guid tenantId, TenantStatus to, string actor, CancellationToken cancellationToken)
    {
        var tenant = await FindAsync(tenantId, cancellationToken);
        Require(tenant, to);
        tenant.ChangeStatus(to);
        await AuditAsync(tenantId, $"status-{to.ToString().ToLowerInvariant()}", actor, null);
        await SaveAndPublishAsync(tenantId, cancellationToken);
        return await GetAsync(tenantId, cancellationToken);
    }

    private static void Require(CatalogTenant tenant, TenantStatus to)
    {
        if (!TenantLifecycle.CanMove(tenant.Status, to))
            throw new BusinessRuleException(TenancyErrors.InvalidTransition, $"{tenant.Status} → {to} is not allowed.");
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private async Task SaveAndPublishAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await catalog.SaveChangesAsync(cancellationToken);
        await tenancy.SaveChangesAsync(cancellationToken);
        await changes.PublishAsync(tenantId, cancellationToken);
    }

    private Task AuditAsync(Guid tenantId, string kind, string actor, object? details)
    {
        tenancy.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, tenantId, kind, actor, details is null ? null : JsonSerializer.Serialize(details)));
        return Task.CompletedTask;
    }

    private async Task<CatalogTenant> FindAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await catalog.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
        ?? throw new NotFoundException(TenancyErrors.TenantNotFound);

    private async Task<Dictionary<Guid, List<string>>> ModulesByTenantAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken)
    {
        var rows = await (from tm in catalog.TenantModules
                          join m in catalog.Modules on tm.ModuleId equals m.ModuleId
                          where tenantIds.Contains(tm.TenantId)
                          select new { tm.TenantId, m.ModuleKey }).ToListAsync(cancellationToken);
        var result = tenantIds.ToDictionary(id => id, _ => new List<string>());
        foreach (var row in rows)
            result[row.TenantId].Add(row.ModuleKey);
        foreach (var list in result.Values)
            list.Sort(StringComparer.Ordinal);
        return result;
    }

    private static Guid ModuleId(string moduleKey) =>
        Modules.All.SingleOrDefault(m => m.Key == moduleKey)?.Id ?? throw new BusinessRuleException(TenancyErrors.UnknownPlan, $"Unknown module '{moduleKey}'.");

    private static TenantSummary ToSummary(CatalogTenant t, List<string> modules) =>
        new(t.Id, t.Slug, t.DisplayName, t.Status, t.Plan, modules, t.DeleteAfter, t.RequireSupportApproval);

    private string[] CallbackUris(string slug)
    {
        var origin = tenancyOptions.Value.TenantOrigin(slug);
        return [$"{origin}/bff/signin-oidc", $"{origin}/bff/signout-callback-oidc"];
    }
}
