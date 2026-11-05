using System.Net;
using Saaskit.Base.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Saaskit.Base.Tests;

/// <summary>The module hosted exactly as in production, on every database provider in platform.yaml, with real migrations and tenants.</summary>
[Trait("service", "base")]
public sealed class BaseApiTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task The_module_starts_and_answers_its_health_check(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<BaseModule>(databases, provider, null, "demo");

        (await host.Client("demo").GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_tenants_database_has_the_modules_schema_migrated_and_no_business_tables(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<BaseModule>(databases, provider, null, "demo");

        await host.InTenantAsync("demo", async sp =>
        {
            var db = sp.GetRequiredService<BaseDbContext>();
            (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
            (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldNotBeEmpty();
            // Only the platform's own tables (outbox, inbox, ...) until a product adds its master data.
            db.Model.GetEntityTypes().Select(e => e.GetTableName()).ShouldNotContain(name => name != null && name.StartsWith("Ms", StringComparison.Ordinal));
        });
    }
}
