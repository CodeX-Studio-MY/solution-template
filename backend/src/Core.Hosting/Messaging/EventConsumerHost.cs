using System.Text;
using Acme.Core.Configuration;
using Acme.Core.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Acme.Core.Hosting.Messaging;

/// <summary>
/// Runs every registered consumer. For each one it declares the quorum queue, three retry queues (growing delays that
/// dead-letter back to the main queue) and a dead-letter queue, then consumes with manual acknowledgements and bounded prefetch.
/// A failing message is retried with backoff; after the last attempt it goes to the dead-letter queue with the error.
/// </summary>
internal sealed partial class EventConsumerHost(
    IEnumerable<EventConsumerRegistration> registrations,
    RabbitMqConnection connection,
    ConsumerPipeline pipeline,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<EventConsumerHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var all = registrations.ToList();
        if (all.Count == 0)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var conn = await connection.GetAsync(stoppingToken);
                var channels = new List<IChannel>();
                foreach (var registration in all)
                    channels.Add(await StartAsync(conn, registration, stoppingToken));
                try
                {
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                finally
                {
                    foreach (var channel in channels)
                        await channel.DisposeAsync();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // The broker may still be starting. Declarations are idempotent, so start over.
                LogStartFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task<IChannel> StartAsync(IConnection conn, EventConsumerRegistration registration, CancellationToken cancellationToken)
    {
        var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqConnection.DeclareExchangesAsync(channel, cancellationToken);

        var queue = RabbitMqTopology.Queue(registration.Name);
        var dead = RabbitMqTopology.DeadLetterQueue(queue);
        var quorum = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(queue, RabbitMqTopology.Exchange, registration.Event.RoutingKey, cancellationToken: cancellationToken);
        for (var attempt = 1; attempt <= RabbitMqTopology.RetryDelaysSeconds.Length; attempt++)
        {
            // A message waits out its delay here, then dead-letters through the default exchange back to the main queue.
            await channel.QueueDeclareAsync(RabbitMqTopology.RetryQueue(queue, attempt), durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-message-ttl"] = RabbitMqTopology.RetryDelaysSeconds[attempt - 1] * 1000,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = queue,
            }, cancellationToken: cancellationToken);
        }
        await channel.QueueDeclareAsync(dead, durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(dead, RabbitMqTopology.DeadLetterExchange, queue, cancellationToken: cancellationToken);

        await channel.BasicQosAsync(0, (ushort)options.Value.Prefetch, global: false, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, registration, queue, delivery, cancellationToken);
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken);
        return channel;
    }

    private async Task HandleAsync(IChannel channel, EventConsumerRegistration registration, string queue, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var headers = delivery.BasicProperties.Headers;
        var attempt = Header(headers, RabbitMqTopology.AttemptHeader) is { } a && int.TryParse(a, out var n) ? n : 0;
        try
        {
            var tenantId = Guid.Parse(Header(headers, RabbitMqTopology.TenantHeader) ?? throw new InvalidOperationException("The message has no tenant."));
            var envelope = new EventEnvelope(
                Guid.Parse(delivery.BasicProperties.MessageId ?? throw new InvalidOperationException("The message has no ID.")),
                registration.Event.Name,
                registration.Event.Version,
                tenantId,
                DateTime.TryParse(Header(headers, RabbitMqTopology.OccurredHeader), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var occurred) ? occurred : time.GetUtcNow().UtcDateTime,
                Header(headers, RabbitMqTopology.ProducerHeader) ?? "unknown",
                Header(headers, RabbitMqTopology.TraceParentHeader),
                Encoding.UTF8.GetString(delivery.Body.Span));

            var outcome = await pipeline.ProcessAsync(registration, envelope, cancellationToken);
            if (outcome == ConsumeOutcome.Retry)
                await RetryOrDeadLetterAsync(channel, queue, delivery, attempt, "The tenant is read-only for now.", forceRetry: true, cancellationToken);
            else
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(logger, ex, registration.Name, attempt + 1);
            await RetryOrDeadLetterAsync(channel, queue, delivery, attempt, ex.Message, forceRetry: false, cancellationToken);
        }
    }

    private async Task RetryOrDeadLetterAsync(IChannel channel, string queue, BasicDeliverEventArgs delivery, int attempt, string error, bool forceRetry, CancellationToken cancellationToken)
    {
        var next = attempt + 1;
        var properties = new BasicProperties(delivery.BasicProperties)
        {
            Headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [RabbitMqTopology.AttemptHeader] = next.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [RabbitMqTopology.ErrorHeader] = error.Length > 500 ? error[..500] : error,
                [RabbitMqTopology.RoutingKeyHeader] = delivery.BasicProperties.Type ?? delivery.RoutingKey,   // the event's key, not the retry queue's
            },
        };

        if (forceRetry || next < options.Value.MaxAttempts)
        {
            var delayIndex = Math.Min(next, RabbitMqTopology.RetryDelaysSeconds.Length);
            await channel.BasicPublishAsync(string.Empty, RabbitMqTopology.RetryQueue(queue, delayIndex), mandatory: false, properties, delivery.Body, cancellationToken);
        }
        else
        {
            await channel.BasicPublishAsync(RabbitMqTopology.DeadLetterExchange, queue, mandatory: false, properties, delivery.Body, cancellationToken);
        }
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
    }

    private static string? Header(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value)
            ? value switch { byte[] bytes => Encoding.UTF8.GetString(bytes), null => null, var other => other.ToString() }
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumers could not start; retrying")]
    private static partial void LogStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Consumer} failed on attempt {Attempt}")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string consumer, int attempt);
}
