using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Errors;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Core.Time;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the kernel services. Called by <c>AddPlatform</c>; hosts never call it directly.</summary>
    public static IServiceCollection AddPlatformKernel(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<DatabaseOptions>(configuration);
        services.AddPlatformOptions<TenancyOptions>(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentPrincipal, HttpCurrentPrincipal>();

        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantClock, TenantClock>();
        services.TryAddSingleton<ITenantConnectionResolver, TenantConnectionResolver>();
        services.TryAddSingleton<IDatabaseProviderAccessor, DatabaseProviderAccessor>();
        services.AddScoped<IInterceptor, AuditSaveChangesInterceptor>();

        services.AddProblemDetails();
        services.AddExceptionHandler<PlatformExceptionHandler>();
        return services;
    }
}
