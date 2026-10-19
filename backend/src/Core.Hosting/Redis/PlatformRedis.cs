using Saaskit.Core.Configuration;
using Saaskit.Core.Security;
using Saaskit.Core.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Saaskit.Core.Hosting.Redis;

/// <summary>The Redis-protocol store (Valkey locally): access versions, tenant change notifications, rate limits.</summary>
public static class PlatformRedis
{
    public static IServiceCollection AddPlatformRedis(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<RedisOptions>(configuration);
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString);
            // Keep retrying in the background: a Redis outage must not stop the host from starting.
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<IAccessVersionStore, RedisAccessVersionStore>();
        services.AddSingleton<ITenantChangeNotifier, RedisTenantChangeNotifier>();
        services.AddHostedService<TenantChangeSubscriber>();
        services.AddSingleton<RedisRateLimiter>();
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: [HealthTags.Ready]);
        return services;
    }

    /// <summary>Key prefix for platform keys; tenant-scoped keys start with <c>{tenantId}:</c> after it.</summary>
    internal static string Key(string suffix) => $"{Platform.PlatformInfo.Product}:{suffix}";
}

internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (RedisException ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unavailable.", ex);
        }
    }
}
