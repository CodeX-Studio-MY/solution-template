using Acme.Core.Data;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Data.SqlServer;
using Acme.Core.Platform;
using Acme.Migrator;

namespace Acme.Architecture.Tests;

public sealed class DataOwnershipTests
{
    public static TheoryData<string> Providers() => [.. PlatformInfo.DatabaseProviders];

    [Theory]
    [MemberData(nameof(Providers))]
    public void No_DbContext_maps_another_schema(string providerName)
    {
        var provider = Provider(providerName);
        foreach (var target in MigrationTargets.Catalog.Concat(MigrationTargets.Tenant))
        {
            using var db = target.Create(provider, ConnectionString(providerName));
            var foreign = db.Model.GetEntityTypes()
                .Select(e => new { Entity = e.ClrType.Name, Schema = Microsoft.EntityFrameworkCore.RelationalEntityTypeExtensions.GetSchema(e) })
                .Where(e => e.Schema != db.Schema)
                .ToList();
            foreign.ShouldBeEmpty($"{db.GetType().Name} (schema {db.Schema}) maps tables in other schemas");
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Each_schema_has_exactly_one_owner_per_database(string providerName)
    {
        var provider = Provider(providerName);
        foreach (var targets in new[] { MigrationTargets.Catalog, MigrationTargets.Tenant })
        {
            var schemas = targets.Select(t =>
            {
                using var db = t.Create(provider, ConnectionString(providerName));
                return db.Schema;
            }).ToList();
            schemas.Distinct(StringComparer.Ordinal).Count().ShouldBe(schemas.Count);
        }
    }

    private static IDatabaseProvider Provider(string name) =>
        name == SqlServerDatabaseProvider.ProviderName ? new SqlServerDatabaseProvider() : new PostgreSqlDatabaseProvider();

    private static string ConnectionString(string name) =>
        name == SqlServerDatabaseProvider.ProviderName ? "Server=model-only;Database=x" : "Host=model-only;Database=x";
}
