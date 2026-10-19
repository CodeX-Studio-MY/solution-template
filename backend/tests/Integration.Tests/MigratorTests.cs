using Saaskit.Core;
using Saaskit.Core.Catalog;
using Saaskit.Core.Catalog.Entities;
using Saaskit.Core.Data;
using Saaskit.Core.Data.PostgreSql;
using Saaskit.Core.Data.SqlServer;
using Saaskit.Core.Platform;
using Saaskit.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Saaskit.Integration.Tests;

[Trait("service", "migrator")]
public sealed class MigratorTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Migrates_the_catalog_and_every_tenant_and_can_rerun(string provider)
    {
        await using var services = BuildMigrator(provider);
        var runner = services.GetRequiredService<MigrationRunner>();

        await runner.MigrateCatalogAsync(Ct);

        var tenantIds = new List<Guid>();
        await using (var scope = services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            (await catalog.Modules.CountAsync(Ct)).ShouldBe(Modules.All.Count);
            (await catalog.PlanModules.CountAsync(Ct)).ShouldBe(Modules.All.Sum(m => m.Plans.Count));

            foreach (var slug in new[] { "alpha", "beta" })
            {
                var tenant = new CatalogTenant(slug, slug, Plans.All[0], PlatformInfo.DefaultTimeZone, PlatformInfo.DefaultCurrency, "en",
                    new TenantPlacementData { Provider = provider, ServerPool = "default", DatabaseName = Databases.UniqueName(slug) });
                catalog.Tenants.Add(tenant);
                await catalog.SaveChangesAsync(Ct);
                tenantIds.Add(tenant.Id);
            }
        }

        (await runner.MigrateAllTenantsAsync(Ct)).ShouldBeEmpty();
        // Rerunning is safe and resumes: nothing is pending.
        (await runner.MigrateAllTenantsAsync(Ct)).ShouldBeEmpty();

        await using (var scope = services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var states = await catalog.MigrationStates.Where(s => tenantIds.Contains(s.TenantId)).ToListAsync(Ct);
            states.Count.ShouldBe(tenantIds.Count * MigrationTargets.Tenant.Count);
            states.ShouldAllBe(s => s.Outcome == MigrationOutcome.Succeeded && s.LastMigration != null);
        }
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task The_catalog_rejects_module_keys_that_break_the_pattern(string provider)
    {
        await using var services = BuildMigrator(provider);
        await services.GetRequiredService<MigrationRunner>().MigrateCatalogAsync(Ct);

        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        catalog.Modules.Add(new CatalogModule { ModuleId = Guid.NewGuid(), ModuleKey = "Bad_Key", DisplayName = "Bad" });
        await Should.ThrowAsync<DbUpdateException>(() => catalog.SaveChangesAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_failed_tenant_is_recorded_and_reported(string provider)
    {
        await using var services = BuildMigrator(provider);
        var runner = services.GetRequiredService<MigrationRunner>();
        await runner.MigrateCatalogAsync(Ct);

        await using (var scope = services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.Tenants.Add(new CatalogTenant("broken", "Broken", Plans.All[0], PlatformInfo.DefaultTimeZone, PlatformInfo.DefaultCurrency, "en",
                new TenantPlacementData { Provider = provider, ServerPool = "missing-pool", DatabaseName = "x" }));
            await catalog.SaveChangesAsync(Ct);
        }

        // The pool isn't configured, so this tenant fails; the run records and reports it instead of stopping.
        (await runner.MigrateAllTenantsAsync(Ct)).ShouldBe(["broken"]);

        await using var check = services.CreateAsyncScope();
        var states = await check.ServiceProvider.GetRequiredService<CatalogDbContext>().MigrationStates.ToListAsync(Ct);
        states.ShouldNotBeEmpty();
        states.ShouldAllBe(s => s.Outcome == MigrationOutcome.Failed && s.Error!.Contains("missing-pool"));
    }

    private ServiceProvider BuildMigrator(string provider)
    {
        var server = databases.ServerConnectionString(provider);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["Database:CatalogConnectionString"] = databases.NewDatabase(provider, "catalog"),
            ["Database:Servers:default"] = server,
            ["Tenancy:BaseDomain"] = "localhost",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPlatformKernel(configuration);
        services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();
        services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
        services.AddTenantCatalog();
        services.AddSingleton<MigrationRunner>();
        return services.BuildServiceProvider();
    }
}
