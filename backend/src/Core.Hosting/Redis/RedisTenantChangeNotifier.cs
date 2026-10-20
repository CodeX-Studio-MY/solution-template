using Farm360.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Farm360.Core.Hosting.Redis;

internal sealed class RedisTenantChangeNotifier(IConnectionMultiplexer redis, IServiceProvider services) : ITenantChangeNotifier
{
    internal static readonly RedisChannel Channel = RedisChannel.Literal(PlatformRedis.Key("tenant-changed"));

    public async Task PublishAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Drop this instance's copies immediately; other instances hear it over pub/sub.
        TenantChangeSubscriber.Notify(services, tenantId);
        await redis.GetSubscriber().PublishAsync(Channel, tenantId.ToString());
    }
}

/// <summary>Drops cached tenant data (directory entries, settings, flags) on every instance when a tenant changes.</summary>
internal sealed class TenantChangeSubscriber(IConnectionMultiplexer redis, IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) =>
        await redis.GetSubscriber().SubscribeAsync(RedisTenantChangeNotifier.Channel, (_, message) =>
        {
            if (Guid.TryParse(message.ToString(), out var tenantId))
                Notify(services, tenantId);
        });

    public Task StopAsync(CancellationToken cancellationToken) =>
        redis.GetSubscriber().UnsubscribeAsync(RedisTenantChangeNotifier.Channel);

    internal static void Notify(IServiceProvider services, Guid tenantId)
    {
        services.GetRequiredService<ITenantDirectory>().Invalidate(tenantId);
        foreach (var listener in services.GetServices<ITenantChangeListener>())
            listener.OnTenantChanged(tenantId);
    }
}
