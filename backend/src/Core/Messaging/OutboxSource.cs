using Acme.Core.Data;
using Acme.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Core.Messaging;

/// <summary>Publishes one module's pending outbox rows for one tenant, in order, at most one relay at a time per tenant and module.</summary>
internal sealed class OutboxSource<TContext>(
    string schema,
    IServiceScopeFactory scopes,
    IMessageBroker broker,
    TimeProvider time) : IOutboxSource
    where TContext : ModuleDbContext
{
    public const int BatchSize = 100;

    public string Schema { get; } = schema;

    public async Task<int> RelayAsync(TenantInfo tenant, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        if (!await db.OutboxMessages.AnyAsync(o => o.PublishedOn == null, cancellationToken))
            return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Other instances may be relaying the same tenant: only one at a time, and the loser finds nothing left to do.
        await scope.ServiceProvider.GetRequiredService<IDistributedLock>().AcquireAsync(db, $"outbox:{Schema}", cancellationToken);

        var pending = await db.OutboxMessages.Where(o => o.PublishedOn == null).OrderBy(o => o.Id).Take(BatchSize).ToListAsync(cancellationToken);
        var published = 0;
        foreach (var message in pending)
        {
            await broker.PublishAsync(
                new EventEnvelope(message.EventId, message.Name, message.Version, tenant.Id, message.OccurredOn, Schema, message.TraceParent, message.Payload),
                cancellationToken);
            message.MarkPublished(time.GetUtcNow().UtcDateTime);
            published++;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return published;
    }
}
