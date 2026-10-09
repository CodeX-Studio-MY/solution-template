using Acme.Core.Data;
using Acme.Core.Http;
using Acme.Core.Messaging;
using Acme.PurchaseRequest.Application;
using Acme.PurchaseRequest.Domain;
using Microsoft.EntityFrameworkCore;

namespace Acme.PurchaseRequest.Infrastructure;

internal sealed class RequestRepository(PurchaseRequestDbContext db, ISequenceService sequences, IEventPublisher events) : IRequestRepository
{
    public Task<Domain.PurchaseRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Requests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<Page<Domain.PurchaseRequest>> ListAsync(PageRequest page, RequestStatus? status, Guid? requesterId, CancellationToken cancellationToken = default)
    {
        var query = db.Requests.AsNoTracking().AsQueryable();
        if (status is not null)
            query = query.Where(r => r.Status == status);
        if (requesterId is not null)
            query = query.Where(r => r.RequesterId == requesterId);
        return await query.OrderByDescending(r => r.CreatedOn).ThenBy(r => r.Id).ToPageAsync(page, cancellationToken);
    }

    public void Add(Domain.PurchaseRequest request) => db.Requests.Add(request);

    public async Task<long> NextSequenceAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        // The counter row is updated in the same transaction as the request, so a failed save doesn't burn a number twice.
        if (db.Database.CurrentTransaction is null)
            await db.Database.BeginTransactionAsync(cancellationToken);
        return await sequences.NextAsync(db, sequenceName, cancellationToken);
    }

    public string ETagOf(Domain.PurchaseRequest request) => db.GetETag(request);

    public void ApplyIfMatch(Domain.PurchaseRequest request, string? ifMatch) => db.ApplyIfMatch(request, ifMatch);

    public void Publish<TEvent>(TEvent integrationEvent)
        where TEvent : class => events.Publish(db, integrationEvent);

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await db.SaveChangesAsync(cancellationToken);
        if (db.Database.CurrentTransaction is { } transaction)
            await transaction.CommitAsync(cancellationToken);
    }
}
