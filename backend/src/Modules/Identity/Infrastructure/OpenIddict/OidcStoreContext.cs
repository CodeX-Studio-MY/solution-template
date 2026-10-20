using System.Runtime.CompilerServices;
using Farm360.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Farm360.Identity.Infrastructure.OpenIddict;

/// <summary>
/// Chooses where authorizations and tokens live: the current tenant's database, or the catalog for platform principals
/// with no tenant. Remembers each entity's database, so updates go back where the entity came from.
/// </summary>
internal sealed class OidcStoreContext(IServiceProvider services, ITenantContext tenant)
{
    private readonly ConditionalWeakTable<object, DbContext> _origins = new();

    public DbContext Current =>
        tenant.HasTenant ? services.GetRequiredService<IdentityDbContext>() : services.GetRequiredService<IdentityCatalogDbContext>();

    public DbContext Catalog => services.GetRequiredService<IdentityCatalogDbContext>();

    public T Track<T>(T entity, DbContext context)
        where T : class
    {
        _origins.AddOrUpdate(entity, context);
        return entity;
    }

    public DbContext OriginOf(object entity) => _origins.TryGetValue(entity, out var context) ? context : Current;
}
