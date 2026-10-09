using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Core.Data;

namespace Acme.Core.Messaging;

/// <summary>The JSON used for event payloads: camelCase, enums as strings. The same everywhere, so any version of any consumer can read it.</summary>
public static class EventJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

internal sealed class OutboxEventPublisher(TimeProvider time) : IEventPublisher
{
    public void Publish<TEvent>(ModuleDbContext db, TEvent integrationEvent)
        where TEvent : class
    {
        var descriptor = EventRegistry.For<TEvent>();
        db.OutboxMessages.Add(OutboxMessage.Create(
            descriptor,
            JsonSerializer.Serialize(integrationEvent, EventJson.Options),
            time.GetUtcNow().UtcDateTime,
            Activity.Current?.Id));   // W3C traceparent, so the trace continues across the queue
    }
}
