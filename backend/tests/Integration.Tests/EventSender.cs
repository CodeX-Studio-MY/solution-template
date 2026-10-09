using System.Text;
using Acme.Core.Hosting.Messaging;
using Acme.Core.Messaging;
using Acme.Core.Platform;
using RabbitMQ.Client;

namespace Acme.Integration.Tests;

/// <summary>Sends an event to the exchange the way a producer in another service would.</summary>
internal static class EventSender
{
    public static async Task SendAsync<TEvent>(Databases databases, Guid tenantId, TEvent integrationEvent, Guid? eventId = null, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var key = EventRegistry.For<TEvent>().RoutingKey;
        await using var connection = await new ConnectionFactory { Uri = new Uri(databases.RabbitMqConnectionString) }.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        await RabbitMqConnection.DeclareExchangesAsync(channel, cancellationToken);
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = (eventId ?? Guid.NewGuid()).ToString(),
            Type = key,
            Headers = new Dictionary<string, object?> { ["x-tenant-id"] = tenantId.ToString(), ["x-producer"] = "test" },
        };
        var body = System.Text.Json.JsonSerializer.Serialize(integrationEvent, EventJson.Options);
        await channel.BasicPublishAsync($"{PlatformInfo.Product}.events", key, mandatory: false, properties, Encoding.UTF8.GetBytes(body), cancellationToken);
    }
}
