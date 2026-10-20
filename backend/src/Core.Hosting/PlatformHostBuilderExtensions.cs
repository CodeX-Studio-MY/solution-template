using Farm360.Core.Catalog;
using Farm360.Core.Configuration;
using Farm360.Core.Data;
using Farm360.Core.Data.PostgreSql;
using Farm360.Core.Data.SqlServer;
using Farm360.Core.Hosting.Auth;
using Farm360.Core.Hosting.DataProtection;
using Farm360.Core.Hosting.Files;
using Farm360.Core.Hosting.Messaging;
using Farm360.Core.Hosting.Redis;
using Farm360.Core.ServiceCalls;
using Farm360.Core.Composition;
using Farm360.Core.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Farm360.Core.Hosting;

public static class PlatformHostBuilderExtensions
{
    /// <summary>
    /// Registers everything a platform host needs: configuration validation, tenancy, data access,
    /// error handling, observability, health, resilience and service discovery, secure by default.
    /// </summary>
    public static WebApplicationBuilder AddPlatform(this WebApplicationBuilder builder, params PlatformService[] services)
    {
        if (services.Length == 0)
            throw new ArgumentException("A host runs at least one service.", nameof(services));

        var hosted = builder.Services.GetOrAddHostedServices();
        hosted.AddRange(services);

        if (hosted.Count == services.Length)
        {
            ListenOnPlatformPort(builder, services[0]);
            builder.AddPlatformInfrastructure(services[0].Key);
            // Service hosts publish and consume events and store files; the Gateway and the Scheduler need only what they use.
            builder.Services.AddPlatformMessaging(builder.Configuration);
            builder.Services.AddPlatformFiles(builder.Configuration);
            AddSecurity(builder);
        }
        return builder;
    }

    /// <summary>Adds a module to this host: registers its services and, in <c>UsePlatform</c>, maps its endpoints.</summary>
    public static WebApplicationBuilder AddModule<TModule>(this WebApplicationBuilder builder)
        where TModule : IPlatformModule
    {
        var hosted = builder.Services.GetOrAddHostedServices();
        if (!hosted.Contains(TModule.Service))
            throw new InvalidOperationException(
                $"{typeof(TModule).Name} belongs to service '{TModule.Service.Key}', which this host doesn't run. Add it to AddPlatform.");
        TModule.Register(builder);
        builder.Services.AddSingleton(new ModuleEndpoints(TModule.Service, TModule.MapEndpoints, TModule.MapRootEndpoints));
        return builder;
    }

    /// <summary>
    /// The infrastructure every public or internal host shares: observability, configuration validation, tenancy, the catalog,
    /// the database provider, Redis, Data Protection and the JSON convention. Used by <c>AddPlatform</c>, the Gateway and the Scheduler.
    /// </summary>
    public static WebApplicationBuilder AddPlatformInfrastructure(this WebApplicationBuilder builder, string serviceName)
    {
        builder.AddServiceDefaults(serviceName);
        builder.Services.AddPlatformKernel(builder.Configuration);

        // One JSON convention for every API: camelCase names, enums as camelCase strings (the generated TypeScript types match).
        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

        // Both providers are registered; Database:Provider picks the one this environment uses.
        builder.Services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();
        builder.Services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
        var database = builder.Configuration.GetValidatedOptions<DatabaseOptions>();
        IDatabaseProvider current = database.Provider == SqlServerDatabaseProvider.ProviderName
            ? new SqlServerDatabaseProvider()
            : new PostgreSqlDatabaseProvider();
        current.AddServices(builder.Services, database.CatalogConnectionString);

        builder.Services.AddTenantCatalog();
        builder.Services.AddPlatformRedis(builder.Configuration);
        builder.Services.AddServiceClientOptions(builder.Configuration);
        builder.Services.AddPlatformDataProtection(builder.Configuration);
        return builder;
    }

    private static void AddSecurity(WebApplicationBuilder builder)
    {
        // Secure by default: every endpoint requires an authenticated principal unless marked [AllowAnonymous].
        builder.Services.AddPlatformTokenValidation();
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddSingleton<AccessVersionChecker>();
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, PlatformAuthorizationResultHandler>();
    }

    /// <summary>Listens on the service's port from platform.yaml unless the environment sets URLs (Aspire, containers).</summary>
    private static void ListenOnPlatformPort(WebApplicationBuilder builder, PlatformService service)
    {
        var configured = builder.Configuration["urls"] ?? builder.Configuration["http_ports"] ?? builder.Configuration["https_ports"];
        if (string.IsNullOrEmpty(configured))
            builder.WebHost.UseUrls($"http://localhost:{service.Port}");
    }

    internal static List<PlatformService> GetOrAddHostedServices(this IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(HostedPlatformServices))?.ImplementationInstance;
        if (existing is HostedPlatformServices hosted)
            return hosted.Services;
        var created = new HostedPlatformServices();
        services.AddSingleton(created);
        return created.Services;
    }
}

internal sealed record ModuleEndpoints(
    PlatformService Service,
    Action<Microsoft.AspNetCore.Routing.IEndpointRouteBuilder> Map,
    Action<Microsoft.AspNetCore.Routing.IEndpointRouteBuilder> MapRoot);
