using System.ComponentModel.DataAnnotations;

namespace Farm360.Core.Configuration;

[OptionsSection("RabbitMq", "The message broker (RabbitMQ 4.x, quorum queues) and the outbox relay.")]
public sealed record RabbitMqOptions
{
    [Required, Secret]
    [Setting("AMQP connection URI. In the cloud, use the broker's private endpoint.", Example = "amqp://user:password@rabbitmq:5672")]
    public string ConnectionString { get; init; } = "";

    [Range(1, 500)]
    [Setting("How many unacknowledged messages one consumer holds at a time (prefetch).")]
    public int Prefetch { get; init; } = 16;

    [Range(1, 10)]
    [Setting("How many times a failing message is tried (with growing delays) before it goes to the dead-letter queue.")]
    public int MaxAttempts { get; init; } = 3;

    [Range(1, 60)]
    [Setting("The outbox relay's slow sweep over every tenant, in minutes. It finds rows whose signal was lost.")]
    public int OutboxSweepMinutes { get; init; } = 5;

    [Range(1, 64)]
    [Setting("How many tenants the relay publishes for in parallel.")]
    public int RelayParallelism { get; init; } = 4;

    [Range(1, 365)]
    [Setting("How long published outbox rows are kept before the sweep deletes them, in days.")]
    public int OutboxRetentionDays { get; init; } = 7;
}
