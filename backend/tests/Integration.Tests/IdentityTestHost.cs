using System.Net.Http.Headers;
using System.Security.Cryptography;
using Acme.Core.Catalog;
using Acme.Core.Data;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Hosting;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Identity;
using Acme.Identity.Application;
using Acme.Identity.Domain;
using Acme.Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Acme.Integration.Tests;

/// <summary>The Identity module hosted for real: PostgreSQL catalog and tenant databases, Valkey, ES256 keys, OpenIddict.</summary>
internal sealed class IdentityTestHost : IAsyncDisposable
{
    public const string Issuer = "https://login.localhost/";

    private readonly WebApplication _app;
    private readonly Databases _databases;

    private IdentityTestHost(WebApplication app, Databases databases, ECDsa signingKey, IReadOnlyDictionary<string, TenantInfo> tenants)
    {
        _app = app;
        _databases = databases;
        SigningKey = signingKey;
        Tenants = tenants;
    }

    /// <summary>The account emails "sent" so far (invitation and reset links).</summary>
    public CapturingAccountMessages Messages => Services.GetRequiredService<CapturingAccountMessages>();

    /// <summary>The catalog database, shared with other hosts in a test.</summary>
    public string CatalogConnectionString { get; private init; } = "";

    /// <summary>The in-memory server, for wiring other hosts' HTTP clients to it.</summary>
    public TestServer Server => _app.GetTestServer();

    /// <summary>Mints the access tokens of callers of the management API.</summary>
    public TestTokens Callers { get; private init; } = null!;

    /// <summary>A client for the tenant's host (<c>demo.localhost</c>) calling with a token for the given roles.</summary>
    public HttpClient ApiClient(string slug, params (string Module, Role Role)[] roles)
    {
        var token = Callers.Create(
            Tenants[slug].Id,
            PrincipalType.User,
            roles.Select(r => RoleExtensions.ToModuleRole(r.Module, r.Role)),
            audience: PlatformServices.Identity.Audience);
        var client = Client($"{slug}.localhost");
        client.DefaultRequestHeaders.Authorization = Bearer(token);
        return client;
    }

    public ECDsa SigningKey { get; }

    public IReadOnlyDictionary<string, TenantInfo> Tenants { get; }

    public IServiceProvider Services => _app.Services;

    public static Task<IdentityTestHost> StartAsync(Databases databases, params string[] tenantSlugs) =>
        StartAsync(databases, null, tenantSlugs);

    public static async Task<IdentityTestHost> StartAsync(Databases databases, Action<WebApplicationBuilder>? configure, params string[] tenantSlugs)
    {
        var catalog = databases.NewDatabase(PostgreSqlDatabaseProvider.ProviderName, "catalog");
        var provider = new PostgreSqlDatabaseProvider();
        // Exactly the catalog contexts the Migrator applies (generated from platform.yaml).
        foreach (var target in Acme.Migrator.MigrationTargets.Catalog)
        {
            await using var db = target.Create(provider, catalog);
            await db.Database.MigrateAsync();
        }

        var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Database:Provider"] = PostgreSqlDatabaseProvider.ProviderName;
        builder.Configuration["Database:CatalogConnectionString"] = catalog;
        builder.Configuration["Database:Servers:default"] = databases.ServerConnectionString(PostgreSqlDatabaseProvider.ProviderName);
        builder.Configuration["Tenancy:BaseDomain"] = "localhost";
        builder.Configuration["Redis:ConnectionString"] = databases.RedisConnectionString;
        TestSecrets.Apply(builder);
        // One shared Valkey and no client address in the in-memory server: keep the per-address auth limit out of unrelated tests.
        builder.Configuration["RateLimit:AuthPerAddressPerMinute"] = "10000";
        builder.Configuration["TokenServer:SigningKeys:0"] = signingKey.ExportECPrivateKeyPem();
        builder.Configuration["TokenServer:EncryptionKeys:0"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.Configuration["TokenServer:BreachedPasswordCheck"] = "false";
        configure?.Invoke(builder);
        builder.AddPlatform(PlatformServices.Identity).AddModule<IdentityModule>();
        // Calls to the management API are made with tokens signed by a test key the host trusts alongside its own.
        var callers = new TestTokens();
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
            configuration.SigningKeys.Add(callers.SigningKey);
            // Tokens the token server itself issued (service tokens, exchanged tokens) validate against its own public key.
            configuration.SigningKeys.Add(new ECDsaSecurityKey(ECDsa.Create(signingKey.ExportParameters(includePrivateParameters: false))) { KeyId = TokenServerKeyId(signingKey) });
            options.Configuration = configuration;
            options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(configuration);
        });
        builder.Services.AddSingleton<CapturingAccountMessages>();
        builder.Services.AddSingleton<IAccountMessages>(sp => sp.GetRequiredService<CapturingAccountMessages>());
        var app = builder.Build();
        app.UsePlatform();
        await app.StartAsync();

        var tenants = new Dictionary<string, TenantInfo>();
        foreach (var slug in tenantSlugs)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await TestPlatformHost.AddTenantAsync(scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), slug,
                slug.Contains("plus", StringComparison.Ordinal) ? Plans.Plus : Plans.Standard, TenantStatus.Active, databaseName: Databases.UniqueName("tenant"));
            var tenant = (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug))!;
            var connection = scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>().ResolveForMaintenance(tenant);
            await using var tenantDb = DbContextRegistration.Create<IdentityDbContext>(null, provider, connection, PlatformServices.Identity.Schema);
            await tenantDb.Database.MigrateAsync();
            tenants[slug] = tenant;
        }
        return new IdentityTestHost(app, databases, signingKey, tenants) { Callers = callers, CatalogConnectionString = catalog };
    }

    /// <summary>The token server's public signing key, as services see it in the JWKS.</summary>
    public Microsoft.IdentityModel.Tokens.SecurityKey TokenServerPublicKey =>
        new ECDsaSecurityKey(ECDsa.Create(SigningKey.ExportParameters(includePrivateParameters: false))) { KeyId = TokenServerKeyId(SigningKey) };

    private static string TokenServerKeyId(ECDsa key) => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16];

    public HttpClient Client(string host = "login.localhost")
    {
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri($"https://{host}");
        return client;
    }

    /// <summary>Runs <paramref name="action"/> in a scope with the tenant set, like a request for that tenant.</summary>
    public async Task<T> InTenantAsync<T>(string slug, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        // Tenants created after start (for example by onboarding) are looked up in the catalog.
        var tenant = Tenants.TryGetValue(slug, out var known)
            ? known
            : await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(slug) ?? throw new InvalidOperationException($"Unknown tenant {slug}.");
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenant);
        return await action(scope.ServiceProvider);
    }

    public Task InTenantAsync(string slug, Func<IServiceProvider, Task> action) =>
        InTenantAsync<object?>(slug, async sp =>
        {
            await action(sp);
            return null;
        });

    /// <summary>Registers a machine or service principal with a <c>private_key_jwt</c> client and returns its key.</summary>
    public async Task<ECDsa> RegisterMachineAsync(string slug, string clientId, PrincipalType type = PrincipalType.Machine, IEnumerable<(string Module, Role Role)>? roles = null)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(key) { KeyId = Guid.NewGuid().ToString("N") });
        jwk.D = null;   // public part only
        await InTenantAsync(slug, async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var principal = type == PrincipalType.Machine ? Principal.NewMachine(clientId) : Principal.NewService(clientId);
            principal.AttachClient(clientId);
            db.Principals.Add(principal);
            await db.SaveChangesAsync();
            foreach (var (module, role) in roles ?? [])
            {
                db.AccessAssignments.Add(AccessAssignment.ForPrincipal(principal.Id, Modules.All.Single(m => m.Key == module).Id, role));
            }
            await db.SaveChangesAsync();
            await sp.GetRequiredService<IClientRegistry>().RegisterAsync(
                clientId, clientId, Tenants[slug].Id, new ClientCredential(PublicKeyJwk: System.Text.Json.JsonSerializer.Serialize(jwk)), ["client_credentials"]);
        });
        return key;
    }

    /// <summary>Builds a <c>private_key_jwt</c> client assertion (RFC 7523).</summary>
    public static string ClientAssertion(string clientId, ECDsa key, string keyId) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = clientId,
            Audience = Issuer,
            TokenType = "client-authentication+jwt",
            Claims = new Dictionary<string, object> { ["sub"] = clientId, ["jti"] = Guid.NewGuid().ToString("N") },
            Expires = TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(2),
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = keyId }, SecurityAlgorithms.EcdsaSha256),
        });

    /// <summary>The public signing keys, as the JWKS endpoint publishes them.</summary>
    public async Task<JsonWebKeySet> FetchJwksAsync() =>
        new(await Client().GetStringAsync(new Uri("/.well-known/jwks", UriKind.Relative)));

    /// <summary>A cookie-keeping client for walking the sign-in pages.</summary>
    public LoginBrowser Browser(string host = "login.localhost") => new(_app.GetTestServer(), host);

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        SigningKey.Dispose();
        Callers.Dispose();
    }
}

/// <summary>Collects account links instead of emailing them.</summary>
internal sealed class CapturingAccountMessages : IAccountMessages
{
    private readonly List<AccountLink> _links = [];

    public IReadOnlyList<AccountLink> Links
    {
        get
        {
            lock (_links)
                return [.. _links];
        }
    }

    public Task SendAsync(AccountLink link, CancellationToken cancellationToken = default)
    {
        lock (_links)
            _links.Add(link);
        return Task.CompletedTask;
    }
}
