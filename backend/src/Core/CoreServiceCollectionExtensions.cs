using Farm360.Core.Customization;
using Farm360.Core.Configuration;
using Farm360.Core.Data;
using Farm360.Core.Errors;
using Farm360.Core.Localization;
using Farm360.Core.Security;
using Farm360.Core.Tenancy;
using Farm360.Core.Time;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Farm360.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the kernel services. Called by <c>AddPlatform</c>; hosts never call it directly.</summary>
    public static IServiceCollection AddPlatformKernel(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<DatabaseOptions>(configuration);
        services.AddPlatformOptions<TenancyOptions>(configuration);
        services.AddPlatformOptions<AuthOptions>(configuration);
        services.AddPlatformOptions<RateLimitOptions>(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentPrincipal, HttpCurrentPrincipal>();

        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantClock, TenantClock>();
        services.TryAddScoped<Authorization.IAccessEvaluator, Authorization.AccessEvaluator>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, Authorization.PlatformAuthorizationHandler>();
        services.TryAddSingleton<ITenantConnectionResolver, TenantConnectionResolver>();
        services.TryAddSingleton<IDatabaseProviderAccessor, DatabaseProviderAccessor>();
        services.AddScoped<IInterceptor, AuditSaveChangesInterceptor>();
        services.TryAddSingleton<Messaging.IOutboxSignal, Messaging.NoOutboxSignal>();
        services.AddScoped<IInterceptor, Messaging.OutboxSignalInterceptor>();
        services.TryAddSingleton<Messaging.IEventPublisher, Messaging.OutboxEventPublisher>();

        services.AddPlatformLocalization();
        services.AddCustomization();
        services.AddProblemDetails();
        services.AddExceptionHandler<PlatformExceptionHandler>();
        return services;
    }
}
