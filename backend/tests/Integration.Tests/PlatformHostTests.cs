using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Hosting;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Migrator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Acme.Integration.Tests;

/// <summary>The pipeline every host gets from AddPlatform / UsePlatform.</summary>
public sealed class PlatformHostTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_needs_no_dependencies_and_no_authentication()
    {
        await using var app = await StartAsync(catalogConnectionString: "Host=unreachable.invalid;Database=x;Timeout=1");
        var response = await app.GetTestClient().GetAsync("/health/live", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_checks_the_catalog_database()
    {
        await using var up = await StartAsync(await CreateCatalogAsync());
        (await up.GetTestClient().GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var down = await StartAsync(catalogConnectionString: "Host=unreachable.invalid;Database=x;Timeout=1");
        (await down.GetTestClient().GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Every_endpoint_requires_authentication_by_default()
    {
        await using var app = await StartAsync(await CreateCatalogAsync());
        var response = await app.GetTestClient().GetAsync($"{PlatformServices.PurchaseRequest.RoutePrefix}/v1/anything", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_X_Tenant_is_a_403_problem_with_a_stable_code()
    {
        await using var app = await StartAsync(await CreateCatalogAsync());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(PlatformHeaders.Tenant, "demo");

        var response = await app.GetTestClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("platform.tenant-header-not-allowed");
        // Outside Development, no details (and never stack traces) are returned.
        problem.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_tenant_subdomain_is_a_404_problem()
    {
        await using var app = await StartAsync(await CreateCatalogAsync());
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://nobody.localhost");
        var response = await client.GetAsync("/health/live", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_host_refuses_to_start_on_invalid_configuration()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Database:Provider"] = "oracle";
        builder.Configuration["Database:CatalogConnectionString"] = "x";
        Should.Throw<Exception>(() => builder.AddPlatform(PlatformServices.PurchaseRequest));
    }

    private async Task<string> CreateCatalogAsync()
    {
        var connectionString = databases.NewDatabase(PostgreSqlDatabaseProvider.ProviderName, "catalog");
        await using var db = MigrationTargets.Catalog[0].Create(new PostgreSqlDatabaseProvider(), connectionString);
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(db.Database, Ct);
        return connectionString;
    }

    private async Task<WebApplication> StartAsync(string catalogConnectionString) =>
        await StartAsync(catalogConnectionString, databases.RedisConnectionString);

    private static async Task<WebApplication> StartAsync(string catalogConnectionString, string redis)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration["Database:Provider"] = PostgreSqlDatabaseProvider.ProviderName;
        builder.Configuration["Database:CatalogConnectionString"] = catalogConnectionString;
        builder.Configuration["Tenancy:BaseDomain"] = "localhost";
        builder.Configuration["Redis:ConnectionString"] = redis;
        TestSecrets.Apply(builder);
        builder.AddPlatform(PlatformServices.PurchaseRequest);
        var app = builder.Build();
        app.UsePlatform();
        await app.StartAsync();
        return app;
    }
}
