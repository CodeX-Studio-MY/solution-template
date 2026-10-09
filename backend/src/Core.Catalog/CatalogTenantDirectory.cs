using Acme.Core.Catalog.Entities;
using Acme.Core.Configuration;
using Acme.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Acme.Core.Catalog;

/// <summary><see cref="ITenantDirectory"/> over the catalog, with a short-lived in-memory cache.</summary>
internal sealed class CatalogTenantDirectory(
    IServiceScopeFactory scopes,
    IMemoryCache cache,
    IOptions<DatabaseOptions> options) : ITenantDirectory
{
    private TimeSpan CacheDuration => TimeSpan.FromSeconds(options.Value.TenantCacheSeconds);

    public async Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await cache.GetOrCreateAsync(IdKey(tenantId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await LoadAsync(q => q.Where(t => t.Id == tenantId), cancellationToken);
        });

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalised = slug.ToLowerInvariant();
        if (cache.TryGetValue(SlugKey(normalised), out Guid id))
            return await FindByIdAsync(id, cancellationToken);
        var tenant = await LoadAsync(q => q.Where(t => t.Slug == normalised), cancellationToken);
        if (tenant is not null)
        {
            cache.Set(SlugKey(normalised), tenant.Id, CacheDuration);
            cache.Set(IdKey(tenant.Id), tenant, CacheDuration);
        }
        return tenant;
    }

    public async Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.Status != TenantStatus.Deleted)
            .OrderBy(t => t.Slug)
            .ToListAsync(cancellationToken);
        var modules = await EnabledModulesAsync(db, tenants.Select(t => t.Id).ToList(), cancellationToken);
        var limits = await db.PlanLimits.AsNoTracking().ToDictionaryAsync(l => l.Plan, cancellationToken);
        return tenants.Select(t => ToInfo(t, modules, limits.GetValueOrDefault(t.Plan))).ToList();
    }

    public void Invalidate(Guid tenantId) => cache.Remove(IdKey(tenantId));

    private async Task<TenantInfo?> LoadAsync(
        Func<IQueryable<CatalogTenant>, IQueryable<CatalogTenant>> filter,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenant = await filter(db.Tenants.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
            return null;
        var modules = await EnabledModulesAsync(db, [tenant.Id], cancellationToken);
        var limit = await db.PlanLimits.AsNoTracking().SingleOrDefaultAsync(l => l.Plan == tenant.Plan, cancellationToken);
        return ToInfo(tenant, modules, limit);
    }

    private static async Task<ILookup<Guid, string>> EnabledModulesAsync(
        CatalogDbContext db,
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from tenantModule in db.TenantModules
                join module in db.Modules on tenantModule.ModuleId equals module.ModuleId
                where tenantIds.Contains(tenantModule.TenantId)
                select new { tenantModule.TenantId, module.ModuleKey })
            .ToListAsync(cancellationToken);
        return rows.ToLookup(r => r.TenantId, r => r.ModuleKey);
    }

    internal static TenantInfo ToInfo(CatalogTenant tenant, ILookup<Guid, string> modules, PlanLimit? limit) => new(
        tenant.Id,
        tenant.Slug,
        tenant.DisplayName,
        tenant.Status,
        tenant.Plan,
        tenant.TimeZone,
        tenant.Currency,
        tenant.Locale,
        modules[tenant.Id].ToHashSet(StringComparer.Ordinal),
        new TenantPlacement(
            tenant.Placement.Provider,
            tenant.Placement.ServerPool,
            tenant.Placement.DatabaseName,
            tenant.Placement.SecretReference),
        limit is null ? null : new PlanLimits(limit.PerTenantPerMinute, limit.PerPrincipalPerMinute),
        tenant.RequireSupportApproval,
        tenant.DeleteAfter);

    private static string IdKey(Guid id) => $"tenant:id:{id}";

    private static string SlugKey(string slug) => $"tenant:slug:{slug}";
}
