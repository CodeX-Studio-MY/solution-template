using Farm360.Core.Data;
using Farm360.Core.Http;
using Farm360.Core.Platform;
using Farm360.MasterData.Application;
using Farm360.MasterData.Domain;
using Microsoft.EntityFrameworkCore;

namespace Farm360.MasterData.Infrastructure;

internal sealed class FarmRepository(MasterDataDbContext db, IDistributedLock distributedLock) : IFarmRepository
{
    private const string OrderLock = $"{Modules.MasterData}:farm-order";

    // Queries are tracked on purpose: the ETag is read from the tracked row version.
    public Task<Page<Farm>> ListAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        db.Farms.OrderBy(f => f.OrderSequence).ThenBy(f => f.Name).ThenBy(f => f.Id).ToPageAsync(page, cancellationToken);

    public Task<Farm?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Farms.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

    // The soft-delete filter applies, so a deleted farm's code is free again (as in the unique index).
    public Task<bool> CodeInUseAsync(string code, Guid? exceptId, CancellationToken cancellationToken = default) =>
        db.Farms.AnyAsync(f => f.Code == code && f.Id != exceptId, cancellationToken);

    // Tenant databases are separate, so the lock (sp_getapplock, pg_advisory_xact_lock) is already per tenant.
    public async Task InOrderLockAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await distributedLock.AcquireAsync(db, OrderLock, cancellationToken);
        await work();
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => db.Farms.CountAsync(cancellationToken);

    public async Task<int> LastPositionAsync(CancellationToken cancellationToken = default) =>
        await db.Farms.MaxAsync(f => (int?)f.OrderSequence, cancellationToken) ?? 0;

    // One UPDATE for the whole range; the soft-delete filter keeps deleted farms out of it.
    public Task ShiftAsync(int first, int last, int delta, CancellationToken cancellationToken = default) =>
        db.Farms
            .Where(f => f.OrderSequence >= first && f.OrderSequence <= last)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.OrderSequence, f => f.OrderSequence + delta), cancellationToken);

    public void Add(Farm farm) => db.Farms.Add(farm);

    public string ETagOf(Farm farm) => db.GetETag(farm);

    public void ApplyIfMatch(Farm farm, string? ifMatch) => db.ApplyIfMatch(farm, ifMatch);

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
