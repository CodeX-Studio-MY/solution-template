using System.Diagnostics;
using System.Text;
using Farm360.Core.Messaging;
using RabbitMQ.Client;

namespace Farm360.Core.Hosting.Messaging;

/// <summary>
/// Publishes with publisher confirms: <see cref="PublishAsync"/> returns only when the broker has taken responsibility for the
/// message (persisted to a quorum of replicas, or routed to the unrouted queue), so the outbox row can be marked published.
/// </summary>
internal sealed class RabbitMqBroker(RabbitMqConnection connection) : IMessageBroker, IAsyncDisposable
{
    private static readonly ActivitySource Source = new("Farm360.Core.Messaging");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var activity = Source.StartActivity($"publish {envelope.RoutingKey}", ActivityKind.Producer);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = envelope.EventId.ToString(),
            Type = envelope.RoutingKey,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.SpecifyKind(envelope.OccurredOn, DateTimeKind.Utc)).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                [RabbitMqTopology.TenantHeader] = envelope.TenantId.ToString(),
                [RabbitMqTopology.ProducerHeader] = envelope.Producer,
                [RabbitMqTopology.OccurredHeader] = envelope.OccurredOn.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                [RabbitMqTopology.TraceParentHeader] = envelope.TraceParent ?? activity?.Id,
            },
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await ChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(RabbitMqTopology.Exchange, envelope.RoutingKey, mandatory: false, properties, Encoding.UTF8.GetBytes(envelope.Payload), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> ChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;
        var conn = await connection.GetAsync(cancellationToken);
        _channel = await conn.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        await RabbitMqConnection.DeclareExchangesAsync(_channel, cancellationToken);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        _gate.Dispose();
    }
}
