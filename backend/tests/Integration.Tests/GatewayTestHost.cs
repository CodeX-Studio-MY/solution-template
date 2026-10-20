extern alias GatewayApp;

using Farm360.Core.Platform;
using Farm360.Identity.Application;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Forwarder;

namespace Farm360.Integration.Tests;

/// <summary>
/// The real Gateway (YARP and the BFF) in memory, in front of a real Identity host. Browser-side requests go to the Gateway;
/// the Gateway's own calls (OpenID discovery, token renewal, proxying) go to Identity's in-memory server.
/// </summary>
internal sealed class GatewayTestHost : IAsyncDisposable
{
    public const string BffSecret = "bff-test-secret";

    private readonly WebApplicationFactory<GatewayApp::Program> _factory;

    private GatewayTestHost(WebApplicationFactory<GatewayApp::Program> factory, IdentityTestHost identity)
    {
        _factory = factory;
        Identity = identity;
    }

    public IdentityTestHost Identity { get; }

    public IServiceProvider Services => _factory.Services;

    public static async Task<GatewayTestHost> StartAsync(Databases databases, params string[] tenantSlugs)
    {
        var identity = await IdentityTestHost.StartAsync(
            databases,
            builder => builder.Configuration[$"TokenServer:PlatformClientSecrets:{PlatformClients.Bff}"] = BffSecret,
            tenantSlugs);

        // Each tenant's callback is registered on the BFF client, as onboarding does.
        foreach (var slug in tenantSlugs)
        {
            await identity.InTenantAsync(slug, sp => sp.GetRequiredService<IClientRegistry>()
                .SetBffRedirectUrisAsync(PlatformClients.Bff, [$"https://{slug}.localhost/bff/signin-oidc", $"https://{slug}.localhost/bff/signout-callback-oidc"], []));
        }

        var factory = new WebApplicationFactory<GatewayApp::Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.UseSetting("Database:Provider", Databases.DefaultProvider);
            host.UseSetting("Database:CatalogConnectionString", identity.CatalogConnectionString);
            host.UseSetting("Tenancy:BaseDomain", "localhost");
            host.UseSetting("Redis:ConnectionString", databases.RedisConnectionString);
            host.UseSetting("DataProtection:KeyEncryptionKey", TestSecrets.KeyEncryptionKey);
            host.UseSetting("Bff:ClientSecret", BffSecret);
            foreach (var (key, value) in TestSecrets.Settings(databases))
                host.UseSetting(key, value);
            host.ConfigureTestServices(services =>
            {
                // Everything the Gateway calls over HTTP lands on the Identity test server.
                services.Configure<OpenIdConnectOptions>(GatewayScheme, oidc => oidc.BackchannelHttpHandler = identity.Server.CreateHandler());
                services.AddHttpClient(Sessions.SessionTokenServiceClient).ConfigurePrimaryHttpMessageHandler(() => identity.Server.CreateHandler());
                services.AddSingleton<IForwarderHttpClientFactory>(new InMemoryForwarder(identity.Server));
            });
        });
        _ = factory.Server;   // start
        return new GatewayTestHost(factory, identity);
    }

    private const string GatewayScheme = "oidc";

    /// <summary>A browser on a tenant's host (<c>demo.localhost</c>) talking to the Gateway.</summary>
    public LoginBrowser Browser(string host) => new(_factory.Server, host);

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Identity.DisposeAsync();
    }

    private sealed class InMemoryForwarder(TestServer upstream) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(upstream.CreateHandler());
    }

    /// <summary>The name of the Gateway's token-renewal HTTP client (internal to the Gateway project).</summary>
    private static class Sessions
    {
        public const string SessionTokenServiceClient = "bff-token";
    }
}
