using StackExchange.Redis;

namespace Saaskit.Core.Hosting.Redis;

/// <summary>
/// A fixed-window limiter shared by every instance (ASP.NET Core's built-in limiter counts per instance,
/// which would multiply the limit by the number of instances).
/// </summary>
internal sealed class RedisRateLimiter(IConnectionMultiplexer redis, TimeProvider time)
{
    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        return count
        """;

    /// <summary>Counts one request against <paramref name="key"/>. Returns the seconds to wait when over the limit, else null.</summary>
    public async Task<int?> TryAcquireAsync(string key, int permitsPerMinute)
    {
        var now = time.GetUtcNow();
        var window = now.ToUnixTimeSeconds() / 60;
        var count = (long)await redis.GetDatabase().ScriptEvaluateAsync(
            IncrementScript,
            [PlatformRedis.Key($"{key}:rl:{window}")],
            [60_000]);
        if (count <= permitsPerMinute)
            return null;
        return (int)(60 - (now.ToUnixTimeSeconds() % 60)) + 1;
    }
}
