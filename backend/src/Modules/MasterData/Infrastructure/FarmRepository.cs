using Farm360.Core.Data;
using Farm360.Core.Http;
using Farm360.MasterData.Application;
using Farm360.MasterData.Domain;
using Microsoft.EntityFrameworkCore;

namespace Farm360.MasterData.Infrastructure;

internal sealed class FarmRepository(MasterDataDbContext db) : IFarmRepository
{
    // Queries are tracked on purpose: the ETag is read from the tracked row version.
    public Task<Page<Farm>> ListAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        db.Farms.OrderBy(f => f.OrderSequence).ThenBy(f => f.Name).ThenBy(f => f.Id).ToPageAsync(page, cancellationToken);

    public Task<Farm?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Farms.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

    // The soft-delete filter applies, so a deleted farm's code is free again (as in the unique index).
    public Task<bool> CodeInUseAsync(string code, Guid? exceptId, CancellationToken cancellationToken = default) =>
        db.Farms.AnyAsync(f => f.Code == code && f.Id != exceptId, cancellationToken);

    public void Add(Farm farm) => db.Farms.Add(farm);

    public string ETagOf(Farm farm) => db.GetETag(farm);

    public void ApplyIfMatch(Farm farm, string? ifMatch) => db.ApplyIfMatch(farm, ifMatch);

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
