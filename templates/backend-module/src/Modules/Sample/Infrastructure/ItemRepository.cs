using Saaskit.Core.Data;
using Saaskit.Core.Http;
using Saaskit.Sample.Application;
using Saaskit.Sample.Domain;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Sample.Infrastructure;

internal sealed class ItemRepository(SampleDbContext db) : IItemRepository
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
