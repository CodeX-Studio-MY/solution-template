namespace Farm360.Core.Messaging;

/// <summary>An event waiting to be published, saved in the same transaction as the business data.</summary>
public sealed class OutboxMessage
{
    public long Id { get; private set; }

    public Guid EventId { get; private set; }

    public string Name { get; private set; } = "";

    public int Version { get; private set; }

    public string Payload { get; private set; } = "";

    public DateTime OccurredOn { get; private set; }

    public string? TraceParent { get; private set; }

    public DateTime? PublishedOn { get; private set; }

    internal static OutboxMessage Create(EventDescriptor descriptor, string payload, DateTime occurredOn, string? traceParent) => new()
    {
        EventId = Guid.CreateVersion7(),
        Name = descriptor.Name,
        Version = descriptor.Version,
        Payload = payload,
        OccurredOn = occurredOn,
        TraceParent = traceParent,
    };

    public void MarkPublished(DateTime utcNow) => PublishedOn = utcNow;
}

/// <summary>A processed message: (event, consumer) is unique, so a duplicate delivery is recognised and skipped.</summary>
public sealed class InboxMessage
{
    public const int MaxConsumerLength = 200;

    public Guid EventId { get; private set; }

    public string Consumer { get; private set; } = "";

    public DateTime ProcessedOn { get; private set; }

    public static InboxMessage Create(Guid eventId, string consumer, DateTime processedOn) =>
        new() { EventId = eventId, Consumer = consumer, ProcessedOn = processedOn };
}
