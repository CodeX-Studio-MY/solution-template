namespace Saaskit.Core.Tenancy;

/// <summary>
/// Tells every instance that a tenant's entitlements, settings, flags or status changed, so cached copies are dropped
/// within seconds. Removing a module from a plan takes effect immediately, without waiting for tokens to expire.
/// </summary>
public interface ITenantChangeNotifier
{
    Task PublishAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Called on every instance when a tenant change is published.</summary>
public interface ITenantChangeListener
{
    void OnTenantChanged(Guid tenantId);
}
