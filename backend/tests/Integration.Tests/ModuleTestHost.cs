using System.Net.Http.Headers;
using Acme.Core.Catalog;
using Acme.Core.Catalog.Entities;
using Acme.Core.Composition;
using Acme.Core.Hosting;
using Acme.Core.Hosting.Messaging;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Migrator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Acme.Integration.Tests;

/// <summary>
/// One business module hosted exactly as in production (AddPlatform), on the chosen database provider, with real migrations,
/// real RabbitMQ and Valkey, and tenants whose databases are created and migrated by the same code the Migrator uses.
/// Tokens are minted by a test key that the host trusts.
/// </summary>
internal sealed class ModuleTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Databases _databases;

    private ModuleTestHost(WebApplication app, Databases databases, string provider, PlatformService service, TestTokens tokens, IReadOnlyDictionary<string, TenantInfo> tenants)
    {
        _app = app;
        _databases = databases;
        Provider = provider;
        Service = service;
        Tokens = tokens;
        Tenants = tenants;
    }

    public string Provider { get; }

    public PlatformService Service { get; }

    public TestTokens Tokens { get; }

    public IReadOnlyDictionary<string, TenantInfo> Tenants { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>The in-memory server's handler, for clients that aren't HttpClient-based (SignalR).</summary>
    public HttpMessageHandler Handler => _app.GetTestServer().CreateHandler();

    /// <summary>Starts <typeparamref name="TModule"/>; tenants named like "demo-plus" get the Plus plan, the rest Standard.</summary>
    public static async Task<ModuleTestHost> StartAsync<TModule>(Databases databases, string provider, Action<WebApplicationBuilder>? configure = null, params string[] tenantSlugs)
        where TModule : IPlatformModule
    {
        var service = TModule.Service;
        var dbProvider = Databases.Provider(provider);
        var server = databases.ServerConnectionString(provider);
        var catalog = databases.NewDatabase(provider, "catalog");

        // The catalog, migrated like the pipeline does before a release.
        foreach (var target in MigrationTargets.Catalog)
        {
            await using var db = target.Create(dbProvider, catalog);
            await db.Database.MigrateAsync();
        }

        var tokens = new TestTokens();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Database:Provider"] = provider;
        builder.Configuration["Database:CatalogConnectionString"] = catalog;
        builder.Configuration["Database:Servers:default"] = server;
        builder.Configuration["Tenancy:BaseDomain"] = "localhost";
        builder.Configuration["Redis:ConnectionString"] = databases.RedisConnectionString;
        TestSecrets.Apply(builder, databases);
        builder.AddPlatform(service).AddModule<TModule>();
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
            configuration.SigningKeys.Add(tokens.SigningKey);
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
        configure?.Invoke(builder);

        var app = builder.Build();
        app.UsePlatform();
        await app.StartAsync();

        var tenants = new Dictionary<string, TenantInfo>();
        foreach (var slug in tenantSlugs)
        {
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var plan = slug.Contains("plus", StringComparison.Ordinal) ? Plans.Plus : Plans.Standard;
                var tenant = new CatalogTenant(slug, slug, plan, PlatformInfo.DefaultTimeZone, PlatformInfo.DefaultCurrency, "en",
                    new TenantPlacementData { Provider = provider, ServerPool = "default", DatabaseName = Databases.UniqueName("tenant") });
                tenant.ChangeStatus(TenantStatus.Active);
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                foreach (var module in Modules.All.Where(m => m.Plans.Contains(plan)))
                    db.TenantModules.Add(new TenantModule { TenantId = tenant.Id, ModuleId = module.Id });
                await db.SaveChangesAsync();
            }

            var info = (await app.Services.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug))!;
            await dbProvider.EnsureDatabaseAsync(server, info.Placement.DatabaseName);
            var migrated = await ActivatorUtilities.CreateInstance<MigrationRunner>(app.Services).MigrateTenantAsync(info, CancellationToken.None);
            if (!migrated)
                throw new InvalidOperationException($"Migrating tenant {slug} failed.");
            tenants[slug] = info;
        }
        return new ModuleTestHost(app, databases, provider, service, tokens, tenants);
    }

    /// <summary>A client calling a tenant's host (<c>demo.localhost</c>), with a bearer token if given.</summary>
    public HttpClient Client(string slug, string? token = null, string? language = null)
    {
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri($"https://{slug}.localhost");
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (language is not null)
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        return client;
    }

    /// <summary>A token for a user (or machine) of a tenant, with roles in the module (or others).</summary>
    public string Token(string slug, Guid? subject = null, PrincipalType type = PrincipalType.User, params (string Module, Role Role)[] roles) =>
        Tokens.Create(
            Tenants[slug].Id,
            type,
            roles.Select(r => RoleExtensions.ToModuleRole(r.Module, r.Role)),
            audience: Service.Audience,
            subject: (subject ?? Guid.NewGuid()).ToString());

    /// <summary>Runs <paramref name="action"/> in a scope with the tenant set, like a request for it.</summary>
    public async Task<T> InTenantAsync<T>(string slug, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(Tenants[slug]);
        return await action(scope.ServiceProvider);
    }

    public Task InTenantAsync(string slug, Func<IServiceProvider, Task> action) =>
        InTenantAsync<object?>(slug, async sp =>
        {
            await action(sp);
            return null;
        });

    /// <summary>The relay's sweep, run now: publishes anything the signal missed.</summary>
    public async Task SweepOutboxAsync()
    {
        var relay = _app.Services.GetServices<IHostedService>().OfType<OutboxRelayService>().Single();
        await relay.SweepOnceAsync(CancellationToken.None);
    }

    public ValueTask<ModuleTestHostScope> ScopeAsync() => ValueTask.FromResult(new ModuleTestHostScope(_app.Services.CreateAsyncScope()));

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Tokens.Dispose();
    }

    public Databases Databases => _databases;
}

internal sealed class ModuleTestHostScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
