namespace Saaskit.Core.Data;

/// <summary>
/// Atomic per-tenant document numbering, in the same transaction as the business data.
/// Never use <c>count + 1</c>. Implemented once per database provider.
/// </summary>
public interface ISequenceService
{
    /// <summary>Increments and returns the next value of <paramref name="sequenceName"/> in the module's schema.</summary>
    Task<long> NextAsync(ModuleDbContext db, string sequenceName, CancellationToken cancellationToken = default);
}

/// <summary>A lock held until the current database transaction ends. Implemented once per database provider.</summary>
public interface IDistributedLock
{
    /// <summary>Takes an exclusive lock on <paramref name="key"/>. Requires an open transaction on <paramref name="db"/>.</summary>
    Task AcquireAsync(PlatformDbContext db, string key, CancellationToken cancellationToken = default);
}
