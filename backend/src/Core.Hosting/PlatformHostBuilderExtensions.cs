using Acme.Core.Catalog;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Data.SqlServer;
using Acme.Core.Composition;
using Acme.Core.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Core.Hosting;

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
            builder.AddServiceDefaults(services[0].Key);
            AddKernel(builder);
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
        builder.Services.AddSingleton(new ModuleEndpoints(TModule.Service, TModule.MapEndpoints));
        return builder;
    }

    private static void AddKernel(WebApplicationBuilder builder)
    {
        builder.Services.AddPlatformKernel(builder.Configuration);

        // Both providers are registered; Database:Provider picks the one this environment uses.
        builder.Services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();
        builder.Services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
        var database = builder.Configuration.GetValidatedOptions<DatabaseOptions>();
        IDatabaseProvider current = database.Provider == SqlServerDatabaseProvider.ProviderName
            ? new SqlServerDatabaseProvider()
            : new PostgreSqlDatabaseProvider();
        current.AddServices(builder.Services, database.CatalogConnectionString);

        builder.Services.AddTenantCatalog();

        // Secure by default: every endpoint requires an authenticated principal unless marked [AllowAnonymous].
        builder.Services.AddAuthentication(NoCredentialsAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, NoCredentialsAuthenticationHandler>(
                NoCredentialsAuthenticationHandler.SchemeName, configureOptions: null);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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

/// <summary>The services this host runs (one, or all of them in <c>AllInOne.Host</c>).</summary>
public sealed class HostedPlatformServices
{
    internal List<PlatformService> Services { get; } = [];

    public IReadOnlyList<PlatformService> All => Services;
}

internal sealed record ModuleEndpoints(PlatformService Service, Action<Microsoft.AspNetCore.Routing.IEndpointRouteBuilder> Map);
