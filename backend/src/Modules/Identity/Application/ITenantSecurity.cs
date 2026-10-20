namespace Farm360.Identity.Application;

/// <summary>Platform operations on a tenant's security state, called by Tenancy (suspension, offboarding) with a service token.</summary>
public interface ITenantSecurity
{
    /// <summary>Revokes every refresh token and authorization of the current tenant's principals. Never touches another tenant's database.</summary>
    Task<long> RevokeAllTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds and removes exact callback URIs of a tenant on the BFF client (during onboarding and offboarding).</summary>
    Task SetBffRedirectUrisAsync(IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken = default);
}
