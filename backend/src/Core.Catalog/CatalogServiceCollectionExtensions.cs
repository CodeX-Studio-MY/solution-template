using Acme.Core.Data;
using Acme.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Core.Catalog;

public static class CatalogServiceCollectionExtensions
{
    /// <summary>Registers the catalog context and the cached tenant directory. Called by <c>AddPlatform</c>.</summary>
    public static IServiceCollection AddTenantCatalog(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddCatalogDbContext<CatalogDbContext>(CatalogDbContext.SchemaName);
        services.TryAddSingleton<ITenantDirectory, CatalogTenantDirectory>();
        return services;
    }
}
