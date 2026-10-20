using Farm360.Core.Configuration;
using Farm360.Core.Data;
using Farm360.Core.Errors;
using Microsoft.Extensions.Options;

namespace Farm360.Core.Tenancy;

public enum TenantDataAccess
{
    Read,
    Write,
}

/// <summary>Builds a tenant's connection string from its catalog placement. Refuses tenants that aren't available.</summary>
public interface ITenantConnectionResolver
{
    /// <summary>The connection string for <paramref name="tenant"/>, or throws when the tenant can't be used for <paramref name="access"/>.</summary>
    string Resolve(TenantInfo tenant, TenantDataAccess access = TenantDataAccess.Read);

    /// <summary>Resolves for maintenance (the Migrator and onboarding), which also allows tenants that are being provisioned.</summary>
    string ResolveForMaintenance(TenantInfo tenant);
}

internal sealed class TenantConnectionResolver(
    IOptions<DatabaseOptions> options,
    IDatabaseProviderAccessor providers) : ITenantConnectionResolver
{
    public string Resolve(TenantInfo tenant, TenantDataAccess access = TenantDataAccess.Read)
    {
        // Suspended tenants are read-only; pending-deletion, deleted and provisioning tenants are refused.
        var allowed = access == TenantDataAccess.Write ? tenant.CanWrite : tenant.CanRead;
        if (!allowed)
            throw new ForbiddenException(
                tenant.Status == TenantStatus.Suspended ? PlatformErrors.TenantReadOnly : PlatformErrors.TenantUnavailable,
                $"Tenant {tenant.Id} is {tenant.Status}.");
        return Build(tenant);
    }

    public string ResolveForMaintenance(TenantInfo tenant)
    {
        if (tenant.Status == TenantStatus.Deleted)
            throw new ForbiddenException(PlatformErrors.TenantUnavailable, $"Tenant {tenant.Id} is deleted.");
        return Build(tenant);
    }

    private string Build(TenantInfo tenant)
    {
        var placement = tenant.Placement;
        if (!options.Value.Servers.TryGetValue(placement.ServerPool, out var baseConnectionString))
            throw new InvalidOperationException(
                $"Database:Servers:{placement.ServerPool} is not configured (tenant {tenant.Id}).");
        return providers.ForName(placement.Provider)
            .BuildConnectionString(baseConnectionString, placement.DatabaseName, options.Value.MaxPoolSize);
    }
}
