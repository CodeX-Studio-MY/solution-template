using System.Text;
using Farm360.Core.Messaging;
using RabbitMQ.Client;

namespace Farm360.Core.Hosting.Messaging;

/// <summary>Dead-letter viewer and replay. Lists what failed (or found no queue) and moves it back to the exchange once the cause is fixed.</summary>
internal sealed class DeadLetterAdmin(RabbitMqConnection connection, IEnumerable<EventConsumerRegistration> registrations) : IDeadLetterAdmin
{
    private IEnumerable<string> Queues =>
        registrations.Select(r => RabbitMqTopology.DeadLetterQueue(RabbitMqTopology.Queue(r.Name))).Append(RabbitMqTopology.UnroutedQueue).Distinct();

    public async Task<IReadOnlyList<DeadLetter>> ListAsync(int max = 50, CancellationToken cancellationToken = default)
    {
        var conn = await connection.GetAsync(cancellationToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
        var found = new List<DeadLetter>();
        var tags = new List<ulong>();
        foreach (var queue in Queues)
        {
            while (found.Count < max && await channel.BasicGetAsync(queue, autoAck: false, cancellationToken) is { } message)
            {
                tags.Add(message.DeliveryTag);
                var headers = message.BasicProperties.Headers;
                found.Add(new DeadLetter(
                    queue,
                    message.BasicProperties.Type ?? message.RoutingKey,
                    Guid.TryParse(Text(headers, RabbitMqTopology.TenantHeader), out var tenant) ? tenant : null,
                    Text(headers, RabbitMqTopology.ErrorHeader),
                    int.TryParse(Text(headers, RabbitMqTopology.AttemptHeader), out var attempts) ? attempts : 0,
                    Encoding.UTF8.GetString(message.Body.Span)));
            }
        }
        // Look, don't take: everything goes back to the queue.
        foreach (var tag in tags)
            await channel.BasicNackAsync(tag, multiple: false, requeue: true, cancellationToken);
        return found;
    }

    public async Task<int> ReplayAsync(string queue, int max = 50, CancellationToken cancellationToken = default)
    {
        if (!Queues.Contains(queue))
            throw new ArgumentException("That is not a dead-letter queue.", nameof(queue));
        var conn = await connection.GetAsync(cancellationToken);
        await using var channel = await conn.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        var replayed = 0;
        while (replayed < max && await channel.BasicGetAsync(queue, autoAck: false, cancellationToken) is { } message)
        {
            var routingKey = message.BasicProperties.Type ?? Text(message.BasicProperties.Headers, RabbitMqTopology.RoutingKeyHeader) ?? message.RoutingKey;
            var headers = new Dictionary<string, object?>(message.BasicProperties.Headers ?? new Dictionary<string, object?>());
            headers.Remove(RabbitMqTopology.AttemptHeader);   // a fresh run of attempts
            headers.Remove(RabbitMqTopology.ErrorHeader);
            var properties = new BasicProperties(message.BasicProperties) { Headers = headers };
            await channel.BasicPublishAsync(RabbitMqTopology.Exchange, routingKey, mandatory: false, properties, message.Body, cancellationToken);
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken);
            replayed++;
        }
        return replayed;
    }

    private static string? Text(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value)
            ? value switch { byte[] bytes => Encoding.UTF8.GetString(bytes), null => null, var other => other.ToString() }
            : null;
}
