using Acme.Core.Tenancy;

namespace Acme.Core.Customization;

/// <summary>
/// The tenant's stored setting overrides, as the catalog holds them. Implemented by <c>Core.Catalog</c>; modules never see it, they
/// read their typed settings through <see cref="ITenantSettings{T}"/>.
/// </summary>
public interface ITenantSettingsStore
{
    /// <summary>The stored overrides (a JSON object) for a tenant's module, or null when the tenant has none.</summary>
    Task<string?> GetAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default);

    /// <summary>Every module's stored overrides for a tenant.</summary>
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A module's settings for the current tenant: the record's defaults with the tenant's overrides on top (rung 1 of the customization
/// ladder). Cached for a few seconds, so a change takes effect everywhere within moments.
/// </summary>
public interface ITenantSettings<T>
    where T : class, new()
{
    Task<T> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class TenantSettings<T>(ITenantContext tenant, ITenantSettingsStore store) : ITenantSettings<T>
    where T : class, new()
{
    private Task<T>? _loaded;

    public Task<T> GetAsync(CancellationToken cancellationToken = default) => _loaded ??= LoadAsync(cancellationToken);

    private async Task<T> LoadAsync(CancellationToken cancellationToken)
    {
        var descriptor = CustomizationRegistry.SettingsFor<T>();
        var stored = await store.GetAsync(tenant.Tenant.Id, descriptor.ModuleKey, cancellationToken);
        return CustomizationRegistry.Resolve<T>(stored);
    }
}
