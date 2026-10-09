using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFeature;

namespace Acme.Core.Customization;

public static class CustomizationRegistration
{
    /// <summary>The settings, flag and strategy services every host has. Called by the platform kernel.</summary>
    public static IServiceCollection AddCustomization(this IServiceCollection services)
    {
        services.TryAddScoped<IFeatureFlags, OpenFeatureFlags>();
        services.TryAddScoped<FeatureProvider>(sp => new CatalogFeatureProvider(sp.GetRequiredService<ITenantFeatureStore>()));
        return services;
    }

    /// <summary>Makes <typeparamref name="T"/> (a record declared in Contracts with <c>[TenantSettings]</c>) available as <see cref="ITenantSettings{T}"/>.</summary>
    public static IServiceCollection AddTenantSettings<T>(this IServiceCollection services)
        where T : class, new()
    {
        _ = CustomizationRegistry.SettingsFor<T>();   // fails at start-up if the record isn't declared
        services.TryAddScoped<ITenantSettings<T>, TenantSettings<T>>();
        return services;
    }

    /// <summary>
    /// Lets each tenant choose among the keyed registrations of <typeparamref name="TStrategy"/> through a setting.
    /// Register the implementations with <c>AddKeyedScoped&lt;TStrategy, TImpl&gt;(key)</c>.
    /// </summary>
    public static IServiceCollection AddTenantStrategy<TStrategy, TSettings>(this IServiceCollection services, Func<TSettings, string?> select, string defaultKey)
        where TStrategy : class
        where TSettings : class, new()
    {
        services.AddTenantSettings<TSettings>();
        services.TryAddScoped<ITenantStrategy<TStrategy>>(sp => new TenantStrategy<TStrategy, TSettings>(
            sp,
            sp.GetRequiredService<ITenantSettings<TSettings>>(),
            select,
            defaultKey,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TenantStrategy<TStrategy, TSettings>>>()));
        return services;
    }
}
