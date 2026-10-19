using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Saaskit.Core.Customization;

/// <summary>
/// The implementation of <typeparamref name="T"/> the current tenant has chosen (rung 3): the logic itself differs, so it is a
/// keyed service picked by a setting, never an <c>if</c> on the tenant. An unknown key falls back to the default and logs a warning.
/// </summary>
public interface ITenantStrategy<T>
    where T : class
{
    Task<T> GetCurrentAsync(CancellationToken cancellationToken = default);
}

internal sealed partial class TenantStrategy<T, TSettings>(
    IServiceProvider services,
    ITenantSettings<TSettings> settings,
    Func<TSettings, string?> select,
    string defaultKey,
    ILogger<TenantStrategy<T, TSettings>> logger) : ITenantStrategy<T>
    where T : class
    where TSettings : class, new()
{
    public async Task<T> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var key = select(await settings.GetAsync(cancellationToken));
        if (!string.IsNullOrEmpty(key) && services.GetKeyedService<T>(key) is { } chosen)
            return chosen;
        if (!string.IsNullOrEmpty(key) && key != defaultKey)
            LogUnknownKey(logger, typeof(T).Name, key, defaultKey);
        return services.GetRequiredKeyedService<T>(defaultKey);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tenant's {Strategy} '{Key}' doesn't exist; using the default '{Default}'")]
    private static partial void LogUnknownKey(ILogger logger, string strategy, string key, string @default);
}
