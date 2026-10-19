using Saaskit.Core.Configuration;
using Saaskit.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Saaskit.Core.Data;

public static class DbContextRegistration
{
    /// <summary>
    /// Registers a module's DbContext in the tenant database. Each request gets a context for the resolved
    /// tenant's connection. DbContext pooling is deliberately not used, because the context holds tenant state.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.AddScoped(sp =>
        {
            var tenant = sp.GetRequiredService<ITenantContext>().Tenant;
            var provider = sp.GetRequiredService<IDatabaseProviderAccessor>().ForName(tenant.Placement.Provider);
            var connectionString = sp.GetRequiredService<ITenantConnectionResolver>().Resolve(tenant);
            return Create<TContext>(sp, provider, connectionString, schema);
        });
        return services;
    }

    /// <summary>Registers a DbContext in the catalog database (Tenancy and Identity's client registrations only).</summary>
    public static IServiceCollection AddCatalogDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : PlatformDbContext
    {
        services.AddScoped(sp =>
        {
            var provider = sp.GetRequiredService<IDatabaseProviderAccessor>().Current;
            var connectionString = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.CatalogConnectionString;
            return Create<TContext>(sp, provider, connectionString, schema);
        });
        return services;
    }

    /// <summary>Creates a context for an explicit connection (Migrator, onboarding, design-time factories, tests).</summary>
    public static TContext Create<TContext>(
        IServiceProvider? services,
        IDatabaseProvider provider,
        string connectionString,
        string schema)
        where TContext : PlatformDbContext
    {
        var options = BuildOptions<TContext>(provider, connectionString, schema, services?.GetServices<IInterceptor>() ?? []);
        return services is null
            ? (TContext)Activator.CreateInstance(typeof(TContext), options)!
            : ActivatorUtilities.CreateInstance<TContext>(services, options);
    }

    public static DbContextOptions<TContext> BuildOptions<TContext>(
        IDatabaseProvider provider,
        string connectionString,
        string schema,
        IEnumerable<IInterceptor> interceptors)
        where TContext : PlatformDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        var migrationsAssembly = $"{typeof(TContext).Assembly.GetName().Name}.Migrations.{provider.MigrationsAssemblySuffix}";
        provider.Configure(builder, connectionString, schema, migrationsAssembly);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new PlatformOptionsExtension(provider, schema));
        builder.ReplaceService<IModelCacheKeyFactory, PlatformModelCacheKeyFactory>();
        builder.AddInterceptors(interceptors);
        return builder.Options;
    }
}

internal sealed class DatabaseProviderAccessor(IEnumerable<IDatabaseProvider> providers, IOptions<DatabaseOptions> options)
    : IDatabaseProviderAccessor
{
    private readonly Dictionary<string, IDatabaseProvider> _providers = providers.ToDictionary(p => p.Name);

    public IDatabaseProvider Current => ForName(options.Value.Provider);

    public IDatabaseProvider ForName(string name) =>
        _providers.TryGetValue(name, out var provider)
            ? provider
            : throw new InvalidOperationException($"Database provider '{name}' is not registered.");
}
