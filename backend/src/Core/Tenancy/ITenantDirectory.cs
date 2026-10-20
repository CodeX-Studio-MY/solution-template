namespace Farm360.Core.Tenancy;

/// <summary>Looks tenants up in the catalog. Implemented by <c>Core.Catalog</c> with a short-lived cache.</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Every tenant that is not deleted, for jobs and migrations.</summary>
    Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops cached entries for one tenant (after a change is published).</summary>
    void Invalidate(Guid tenantId);
}
