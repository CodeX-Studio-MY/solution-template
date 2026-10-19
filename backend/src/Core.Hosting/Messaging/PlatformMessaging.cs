using Saaskit.Core.Configuration;
using Saaskit.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Saaskit.Core.Hosting.Messaging;

public static class PlatformMessaging
{
    /// <summary>RabbitMQ (quorum queues), the outbox relay and the consumer host. Every host gets them; they idle if the host has no outbox or consumers.</summary>
    public static IServiceCollection AddPlatformMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<RabbitMqOptions>(configuration);
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqBroker>();
        services.TryAddSingleton<IMessageBroker>(sp => sp.GetRequiredService<RabbitMqBroker>());
        services.AddSingleton<IDeadLetterAdmin, DeadLetterAdmin>();
        services.AddSingleton<IOutboxSignal, RedisOutboxSignal>();
        services.AddSingleton<ConsumerPipeline>();
        services.AddHostedService<OutboxRelayService>();
        services.AddHostedService<EventConsumerHost>();
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [HealthTags.Ready]);
        return services;
    }
}

internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var conn = await connection.GetAsync(cancellationToken);
            return conn.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The RabbitMQ connection is closed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unavailable.", ex);
        }
    }
}
