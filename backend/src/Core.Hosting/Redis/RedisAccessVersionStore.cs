using Farm360.Core.Security;
using StackExchange.Redis;

namespace Farm360.Core.Hosting.Redis;

/// <summary>Access versions in Redis: one hash per tenant, field per principal.</summary>
internal sealed class RedisAccessVersionStore(IConnectionMultiplexer redis) : IAccessVersionStore
{
    // Sets each field only when the new version is higher, atomically.
    private const string RaiseScript = """
        for i = 1, #ARGV, 2 do
          local current = tonumber(redis.call('HGET', KEYS[1], ARGV[i]) or '-1')
          if tonumber(ARGV[i + 1]) > current then redis.call('HSET', KEYS[1], ARGV[i], ARGV[i + 1]) end
        end
        return 1
        """;

    public async Task<long?> GetAsync(Guid tenantId, string principalId, CancellationToken cancellationToken = default)
    {
        var value = await redis.GetDatabase().HashGetAsync(Key(tenantId), principalId);
        return value.IsNull ? null : (long)value;
    }

    public async Task SetAsync(Guid tenantId, IReadOnlyDictionary<string, long> versions, CancellationToken cancellationToken = default)
    {
        if (versions.Count == 0)
            return;
        var args = versions.SelectMany(v => new RedisValue[] { v.Key, v.Value }).ToArray();
        await redis.GetDatabase().ScriptEvaluateAsync(RaiseScript, [Key(tenantId)], args);
    }

    private static RedisKey Key(Guid tenantId) => PlatformRedis.Key($"{tenantId}:access-version");
}
