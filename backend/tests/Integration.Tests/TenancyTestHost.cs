using Farm360.Core.Hosting;
using Farm360.Core.Platform;
using Farm360.Core.ServiceCalls;
using Farm360.Migrator;
using Farm360.Tenancy;
using Farm360.Tenancy.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Farm360.Integration.Tests;

/// <summary>
/// The Tenancy and Identity services running together against one catalog, as in production: Tenancy calls Identity over HTTP
/// with its own service token (client credentials), here through Identity's in-memory server.
/// </summary>
internal sealed class TenancyTestHost : IAsyncDisposable
{
    public const string ServiceSecret = "tenancy-service-secret";

    private readonly WebApplication _app;

    private TenancyTestHost(WebApplication app, IdentityTestHost identity)
    {
        _app = app;
        Identity = identity;
    }

    public IdentityTestHost Identity { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<TenancyTestHost> StartAsync(Databases databases, Action<WebApplicationBuilder>? configureTenancy = null)
    {
        var identity = await IdentityTestHost.StartAsync(databases, builder =>
        {
            builder.Configuration[$"TokenServer:PlatformClientSecrets:{PlatformServices.Tenancy.ClientId}"] = ServiceSecret;
            builder.Configuration[$"TokenServer:PlatformClientSecrets:{PlatformClients.Bff}"] = "bff-test-secret";
        });

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Database:Provider"] = Databases.DefaultProvider;
        builder.Configuration["Database:CatalogConnectionString"] = identity.CatalogConnectionString;
        builder.Configuration["Database:Servers:default"] = databases.ServerConnectionString(Databases.DefaultProvider);
        builder.Configuration["Tenancy:BaseDomain"] = "localhost";
        builder.Configuration["Redis:ConnectionString"] = databases.RedisConnectionString;
        TestSecrets.Apply(builder, databases);
        // One shared Valkey and no client address in the in-memory server: keep the per-address auth limit out of unrelated tests.
        builder.Configuration["RateLimit:AuthPerAddressPerMinute"] = "10000";
        builder.Configuration["ServiceClient:ClientSecret"] = ServiceSecret;
        builder.AddPlatform(PlatformServices.Tenancy).AddModule<TenancyModule>();

        // Tenancy's calls to Identity (and its token requests) go to Identity's in-memory server.
        builder.Services.AddHttpClient<IIdentityClient, Farm360.Tenancy.Infrastructure.IdentityClient>().ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler());
        builder.Services.AddHttpClient(PlatformTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler());
        // The pipeline's Migrator, run in-process for the tenant.
        builder.Services.AddScoped<ITenantMigrator, InProcessTenantMigrator>();
        // Tenancy trusts the same test callers as Identity does.
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
            configuration.SigningKeys.Add(identity.Callers.SigningKey);
            configuration.SigningKeys.Add(identity.TokenServerPublicKey);
            options.Configuration = configuration;
            options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(configuration);
        });
        configureTenancy?.Invoke(builder);

        var app = builder.Build();
        app.UsePlatform();
        await app.StartAsync();
        return new TenancyTestHost(app, identity);
    }

    public HttpClient Client(string host = "localhost")
    {
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri($"https://{host}");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await Identity.DisposeAsync();
    }

    private sealed class InProcessTenantMigrator(IServiceProvider services, Farm360.Core.Tenancy.ITenantDirectory directory) : ITenantMigrator
    {
        public async Task MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            var runner = ActivatorUtilities.CreateInstance<MigrationRunner>(services);
            var tenant = await directory.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException("Unknown tenant.");
            if (!await runner.MigrateTenantAsync(tenant, cancellationToken))
                throw new InvalidOperationException("The migrator failed for the tenant.");
        }
    }
}
