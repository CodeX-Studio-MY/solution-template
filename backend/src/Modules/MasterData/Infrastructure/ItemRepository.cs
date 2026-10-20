using Farm360.Core.Data;
using Farm360.Core.Http;
using Farm360.MasterData.Application;
using Farm360.MasterData.Domain;
using Microsoft.EntityFrameworkCore;

namespace Farm360.MasterData.Infrastructure;

internal sealed class ItemRepository(MasterDataDbContext db) : IItemRepository
{
    // Queries are tracked on purpose: the ETag is read from the tracked row version.
    public Task<Page<Item>> ListAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        db.Items.OrderBy(i => i.Name).ThenBy(i => i.Id).ToPageAsync(page, cancellationToken);

    public Task<Item?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Items.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

    public void Add(Item item) => db.Items.Add(item);

    public string ETagOf(Item item) => db.GetETag(item);

    public void ApplyIfMatch(Item item, string? ifMatch) => db.ApplyIfMatch(item, ifMatch);

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
