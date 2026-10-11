using System.Net.Http.Headers;
using Acme.Core.Authorization;
using Acme.Core.Catalog;
using Acme.Core.Catalog.Entities;
using Acme.Core.Composition;
using Acme.Core.Hosting;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Migrator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Acme.Integration.Tests;

/// <summary>Endpoints guarded by each authorization rule, hosted as if they were the purchase-request service.</summary>
internal sealed class ProbeModule : IPlatformModule
{
    public static readonly ModulePermission Approve = new(Modules.PurchaseRequest, "approve", Role.Manager);

    public static PlatformService Service => PlatformServices.PurchaseRequest;

    public static void Register(IHostApplicationBuilder builder)
    {
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/probe/user", () => "ok").RequireModuleRole(Modules.PurchaseRequest, Role.User);
        api.MapPost("/probe/user", () => "ok").RequireModuleRole(Modules.PurchaseRequest, Role.User);
        api.MapGet("/probe/manager", () => "ok").RequireModuleRole(Modules.PurchaseRequest, Role.Manager);
        api.MapGet("/probe/approve", () => "ok").RequirePermission(Approve);
        api.MapGet("/probe/machine", () => "ok").RequireModuleRole(Modules.PurchaseRequest, Role.User).AllowPrincipalTypes(PrincipalType.Machine);
        api.MapGet("/probe/budget", () => "ok").RequireModuleRole(Modules.CostCenterBudget, Role.User);
        api.MapGet("/probe/whoami", (HttpContext http, ITenantContext tenant) => tenant.HasTenant ? tenant.Tenant.Slug : "none");
    }
}

/// <summary>A purchase-request host with a migrated catalog, demo tenants, Valkey and a test token signer.</summary>
internal sealed class TestPlatformHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestPlatformHost(WebApplication app, TestTokens tokens, IReadOnlyDictionary<string, TenantInfo> tenants)
    {
        _app = app;
        Tokens = tokens;
        Tenants = tenants;
    }

    public TestTokens Tokens { get; }

    public IReadOnlyDictionary<string, TenantInfo> Tenants { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<TestPlatformHost> StartAsync(Databases databases, Action<WebApplicationBuilder>? configure = null, PlanLimit? limit = null)
    {
        var catalog = databases.NewDatabase(Databases.DefaultProvider, "catalog");
        await using (var db = MigrationTargets.Catalog[0].Create(Databases.Provider(Databases.DefaultProvider), catalog))
            await db.Database.MigrateAsync();

        var tokens = new TestTokens();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Database:Provider"] = Databases.DefaultProvider;
        builder.Configuration["Database:CatalogConnectionString"] = catalog;
        builder.Configuration["Tenancy:BaseDomain"] = "localhost";
        builder.Configuration["Redis:ConnectionString"] = databases.RedisConnectionString;
        TestSecrets.Apply(builder, databases);
        builder.AddPlatform(PlatformServices.PurchaseRequest).AddModule<ProbeModule>();
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
            configuration.SigningKeys.Add(tokens.SigningKey);
            options.Configuration = configuration;
            options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
        configure?.Invoke(builder);

        var app = builder.Build();
        app.UsePlatform();
        await app.StartAsync();

        // Seed through the host's own context, so audit fields are filled as in production.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await AddTenantAsync(catalogDb, "demo", Plans.Standard, TenantStatus.Active);
            await AddTenantAsync(catalogDb, "demo-plus", Plans.Plus, TenantStatus.Active);
            await AddTenantAsync(catalogDb, "paused", Plans.Standard, TenantStatus.Suspended);
            if (limit is not null)
            {
                catalogDb.PlanLimits.Add(limit);
                await catalogDb.SaveChangesAsync();
            }
        }

        var directory = app.Services.GetRequiredService<ITenantDirectory>();
        var tenants = new Dictionary<string, TenantInfo>();
        foreach (var slug in new[] { "demo", "demo-plus", "paused" })
            tenants[slug] = (await directory.FindBySlugAsync(slug))!;
        return new TestPlatformHost(app, tokens, tenants);
    }

    /// <summary>A client calling as <paramref name="host"/> (for example <c>demo.localhost</c>) with an optional bearer token.</summary>
    public HttpClient Client(string host, string? token = null)
    {
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri($"http://{host}");
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static string Path(string relative) => $"{PlatformServices.PurchaseRequest.RoutePrefix}{relative}";

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Tokens.Dispose();
    }

    internal static async Task AddTenantAsync(CatalogDbContext db, string slug, string plan, TenantStatus status, string? databaseName = null)
    {
        var tenant = new CatalogTenant(slug, slug, plan, PlatformInfo.DefaultTimeZone, PlatformInfo.DefaultCurrency, "en",
            new TenantPlacementData { Provider = Databases.DefaultProvider, ServerPool = "default", DatabaseName = databaseName ?? slug });
        tenant.ChangeStatus(status);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        foreach (var module in Modules.All.Where(m => m.Plans.Contains(plan)))
            db.TenantModules.Add(new TenantModule { TenantId = tenant.Id, ModuleId = module.Id });
        await db.SaveChangesAsync();
    }
}
