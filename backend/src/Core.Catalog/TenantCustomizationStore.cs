using Acme.Core.Configuration;
using Acme.Core.Customization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Acme.Core.Catalog;

/// <summary>Drops cached settings and flags after a change, so the writer's own process sees it at once (others within the cache time).</summary>
public interface ITenantCustomizationCache
{
    void Invalidate(Guid tenantId);
}

/// <summary>Settings and flags from the catalog, cached briefly: they are read on most requests.</summary>
internal sealed class TenantCustomizationStore(
    IServiceScopeFactory scopes,
    IMemoryCache cache,
    IOptions<DatabaseOptions> options) : ITenantSettingsStore, ITenantFeatureStore, ITenantCustomizationCache
{
    private TimeSpan CacheDuration => TimeSpan.FromSeconds(options.Value.TenantCacheSeconds);

    public async Task<string?> GetAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(tenantId, cancellationToken)).GetValueOrDefault(moduleKey);

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        (await cache.GetOrCreateAsync(SettingsKey(tenantId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            return await db.TenantSettings.AsNoTracking().Where(s => s.TenantId == tenantId).ToDictionaryAsync(s => s.ModuleKey, s => s.Json, cancellationToken);
        }))!;

    async Task<IReadOnlyDictionary<string, bool>> ITenantFeatureStore.GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(FeaturesKey(tenantId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            return await db.TenantFeatures.AsNoTracking().Where(f => f.TenantId == tenantId).ToDictionaryAsync(f => f.FeatureKey, f => f.Enabled, cancellationToken);
        }))!;

    public void Invalidate(Guid tenantId)
    {
        cache.Remove(SettingsKey(tenantId));
        cache.Remove(FeaturesKey(tenantId));
    }

    private static string SettingsKey(Guid id) => $"tenant:settings:{id}";

    private static string FeaturesKey(Guid id) => $"tenant:features:{id}";
}
