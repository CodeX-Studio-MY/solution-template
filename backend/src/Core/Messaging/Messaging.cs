using Acme.Core.Data;
using Acme.Core.Tenancy;

namespace Acme.Core.Messaging;

/// <summary>
/// What travels between modules. Every message carries the tenant, the event ID, the event time, the producer and the W3C
/// <c>traceparent</c>, so a trace continues across the queue.
/// </summary>
public sealed record EventEnvelope(
    Guid EventId,
    string Name,
    int Version,
    Guid TenantId,
    DateTime OccurredOn,
    string Producer,
    string? TraceParent,
    string Payload)
{
    public string RoutingKey => $"{Name}.v{Version}";
}

/// <summary>
/// Publishes an integration event as part of the business transaction: the event is saved in the module's outbox table with the
/// business data, and a relay publishes it after commit. Nothing is sent if the transaction rolls back.
/// </summary>
public interface IEventPublisher
{
    void Publish<TEvent>(ModuleDbContext db, TEvent integrationEvent)
        where TEvent : class;
}

/// <summary>Handles one integration event, once. Idempotency comes from the inbox; the handler may write through the module's context.</summary>
public interface IEventConsumer<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>Sends an envelope to the broker with publisher confirms. Returns when the broker has taken responsibility for it.</summary>
public interface IMessageBroker
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// "Tenant X has pending outbox rows in schema S." Published after a commit that wrote outbox rows, so the relay doesn't poll every
/// tenant database. A slow sweep catches any signal that was lost.
/// </summary>
public interface IOutboxSignal
{
    Task SignalAsync(Guid tenantId, string schema, CancellationToken cancellationToken = default);
}

/// <summary>One module's outbox, as the relay sees it: relay the pending rows of one tenant.</summary>
public interface IOutboxSource
{
    string Schema { get; }

    /// <summary>Publishes the tenant's pending outbox rows in order. Returns how many were published.</summary>
    Task<int> RelayAsync(TenantInfo tenant, CancellationToken cancellationToken = default);
}

/// <summary>A message waiting in a dead-letter queue.</summary>
public sealed record DeadLetter(string Queue, string RoutingKey, Guid? TenantId, string? Error, int Attempts, string Payload);

/// <summary>Dead-letter viewer and replay (an operator tool; never called from request paths).</summary>
public interface IDeadLetterAdmin
{
    Task<IReadOnlyList<DeadLetter>> ListAsync(int max = 50, CancellationToken cancellationToken = default);

    /// <summary>Moves up to <paramref name="max"/> messages from dead-letter queues back to the exchange. Returns how many were replayed.</summary>
    Task<int> ReplayAsync(string queue, int max = 50, CancellationToken cancellationToken = default);
}
