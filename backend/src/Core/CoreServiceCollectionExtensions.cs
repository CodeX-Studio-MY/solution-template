using Saaskit.Core.Customization;
using Saaskit.Core.Configuration;
using Saaskit.Core.Data;
using Saaskit.Core.Errors;
using Saaskit.Core.Localization;
using Saaskit.Core.Security;
using Saaskit.Core.Tenancy;
using Saaskit.Core.Time;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Saaskit.Core;

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
