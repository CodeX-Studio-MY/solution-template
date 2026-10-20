using Farm360.Core.Customization;
using Farm360.Core.Data;
using Farm360.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Farm360.Core.Catalog;

public static class CatalogServiceCollectionExtensions
{
    /// <summary>Registers the catalog context and the cached tenant directory. Called by <c>AddPlatform</c>.</summary>
    public static IServiceCollection AddTenantCatalog(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddCatalogDbContext<CatalogDbContext>(CatalogDbContext.SchemaName);
        services.TryAddSingleton<ITenantDirectory, CatalogTenantDirectory>();
        services.TryAddSingleton<TenantCustomizationStore>();
        services.TryAddSingleton<ITenantSettingsStore>(sp => sp.GetRequiredService<TenantCustomizationStore>());
        services.TryAddSingleton<ITenantFeatureStore>(sp => sp.GetRequiredService<TenantCustomizationStore>());
        services.TryAddSingleton<ITenantCustomizationCache>(sp => sp.GetRequiredService<TenantCustomizationStore>());
        return services;
    }
}
