using Acme.Core.Platform;

namespace Acme.Core.Hosting.Messaging;

/// <summary>
/// Names on the broker. One topic exchange carries every event by routing key (<c>{event-name}.v{version}</c>). Each consumer has a
/// quorum queue, retry queues with growing delays that dead-letter back to it, and a dead-letter queue. Events nobody has a queue
/// for yet go to the alternate exchange instead of vanishing, so an operator can replay them once a consumer exists.
/// </summary>
internal static class RabbitMqTopology
{
    public static readonly string Exchange = $"{PlatformInfo.Product}.events";
    public static readonly string DeadLetterExchange = $"{PlatformInfo.Product}.events.dlx";
    public static readonly string UnroutedExchange = $"{PlatformInfo.Product}.events.unrouted";
    public static readonly string UnroutedQueue = $"{PlatformInfo.Product}.events.unrouted";

    /// <summary>Retry delays by attempt: 5 s, 30 s, 3 min, then 3 min again.</summary>
    public static readonly int[] RetryDelaysSeconds = [5, 30, 180];

    public const string AttemptHeader = "x-attempt";
    public const string TenantHeader = "x-tenant-id";
    public const string ProducerHeader = "x-producer";
    public const string TraceParentHeader = "traceparent";
    public const string ErrorHeader = "x-error";
    public const string RoutingKeyHeader = "x-original-routing-key";
    public const string OccurredHeader = "x-occurred-on";

    public static string Queue(string consumerName) => $"{PlatformInfo.Product}.{consumerName}";

    public static string RetryQueue(string queue, int attempt) => $"{queue}.retry.{attempt}";

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";
}
