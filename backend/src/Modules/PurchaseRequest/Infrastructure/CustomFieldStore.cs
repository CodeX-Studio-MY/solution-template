using Acme.Core.Customization;
using Acme.PurchaseRequest.Application;
using Microsoft.EntityFrameworkCore;

namespace Acme.PurchaseRequest.Infrastructure;

internal sealed class CustomFieldStore(PurchaseRequestDbContext db) : ICustomFieldStore
{
    public async Task<IReadOnlyList<CustomFieldDefinition>> ListAsync(string entity, CancellationToken cancellationToken = default) =>
        await db.CustomFieldDefinitions.AsNoTracking().Where(d => d.Entity == entity).ToListAsync(cancellationToken);

    public Task<CustomFieldDefinition?> FindAsync(string entity, string key, CancellationToken cancellationToken = default) =>
        db.CustomFieldDefinitions.SingleOrDefaultAsync(d => d.Entity == entity && d.Key == key, cancellationToken);

    public void Add(CustomFieldDefinition definition) => db.CustomFieldDefinitions.Add(definition);

    public void Remove(CustomFieldDefinition definition) => db.CustomFieldDefinitions.Remove(definition);

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
