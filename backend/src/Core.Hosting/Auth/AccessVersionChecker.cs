using System.Collections.Concurrent;
using System.Globalization;
using Farm360.Core.Configuration;
using Farm360.Core.Errors;
using Farm360.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Farm360.Core.Hosting.Auth;

/// <summary>
/// Compares a token's <c>access_version</c> with the current one. Values are cached in memory for a few seconds.
/// If the store is unavailable, the last known value is used for a grace period; after that it fails closed with 503
/// (not 401), so clients retry instead of signing users out.
/// </summary>
public sealed partial class AccessVersionChecker(
    IAccessVersionStore store,
    TimeProvider time,
    IOptions<AuthOptions> options,
    ILogger<AccessVersionChecker> logger)
{
    private readonly ConcurrentDictionary<(Guid, string), Entry> _cache = new();

    /// <summary>True when the token's version is current. Throws <see cref="AccessCheckUnavailableException"/> when it can't tell.</summary>
    public async Task<bool> IsCurrentAsync(Guid tenantId, string principalId, string? tokenVersion, CancellationToken cancellationToken)
    {
        var token = long.TryParse(tokenVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var current = await CurrentAsync(tenantId, principalId, cancellationToken);
        return current is null || token >= current.Value;
    }

    private async Task<long?> CurrentAsync(Guid tenantId, string principalId, CancellationToken cancellationToken)
    {
        var key = (tenantId, principalId);
        var now = time.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && now - cached.FetchedAt < TimeSpan.FromSeconds(options.Value.AccessVersionCacheSeconds))
            return cached.Version;

        try
        {
            var version = await store.GetAsync(tenantId, principalId, cancellationToken);
            _cache[key] = new Entry(version, now);
            return version;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (cached is not null && now - cached.FetchedAt < TimeSpan.FromSeconds(options.Value.AccessVersionGraceSeconds))
            {
                LogUsingLastKnown(logger, ex);
                return cached.Version;
            }
            throw new AccessCheckUnavailableException(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Access version store unavailable; using the last known value")]
    private static partial void LogUsingLastKnown(ILogger logger, Exception exception);

    private sealed record Entry(long? Version, DateTimeOffset FetchedAt);
}

/// <summary>The access check can't be made (503): the client should retry, not sign the user out.</summary>
public sealed class AccessCheckUnavailableException(Exception inner)
    : PlatformException(PlatformErrors.AccessCheckUnavailable, "The access version store is unavailable.", inner);
