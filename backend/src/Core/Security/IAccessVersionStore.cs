namespace Farm360.Core.Security;

/// <summary>
/// The current <c>access_version</c> of each principal. Identity increments it when assignments, group membership
/// or a group's roles change; every service rejects tokens carrying an older value (401), so access changes take effect quickly.
/// </summary>
public interface IAccessVersionStore
{
    /// <summary>The principal's current version, or null when none is recorded. Throws when the store is unavailable.</summary>
    Task<long?> GetAsync(Guid tenantId, string principalId, CancellationToken cancellationToken = default);

    /// <summary>Records versions (only ever raising them).</summary>
    Task SetAsync(Guid tenantId, IReadOnlyDictionary<string, long> versions, CancellationToken cancellationToken = default);
}
