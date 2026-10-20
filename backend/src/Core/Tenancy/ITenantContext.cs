namespace Farm360.Core.Tenancy;

/// <summary>The tenant the current request, message or job runs for. Scoped.</summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    /// <summary>The current tenant. Throws when no tenant has been resolved.</summary>
    TenantInfo Tenant { get; }

    Guid TenantId => Tenant.Id;
    string TenantSlug => Tenant.Slug;
    TenantStatus Status => Tenant.Status;
    string TimeZone => Tenant.TimeZone;
    string Currency => Tenant.Currency;
    string Locale => Tenant.Locale;
    IReadOnlySet<string> EnabledModules => Tenant.EnabledModules;
}

/// <summary>Sets the tenant for the current scope. Used by tenant resolution, message consumers and jobs.</summary>
public interface ITenantContextSetter
{
    void SetTenant(TenantInfo tenant);
}

internal sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    private TenantInfo? _tenant;

    public bool HasTenant => _tenant is not null;

    public TenantInfo Tenant => _tenant ?? throw new InvalidOperationException("No tenant has been resolved for this scope.");

    public void SetTenant(TenantInfo tenant)
    {
        if (_tenant is not null && _tenant.Id != tenant.Id)
            throw new InvalidOperationException("The tenant of a scope cannot change.");
        _tenant = tenant;
    }
}
