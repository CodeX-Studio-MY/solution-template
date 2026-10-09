using Acme.Core.Data.PostgreSql;
using Acme.Core.Data.SqlServer;
using Acme.Migrator;
using Microsoft.EntityFrameworkCore;

namespace Acme.Integration.Tests;

/// <summary>
/// The model and the migrations of every context never drift apart, for every database provider (SAAS_TEMPLATE_PROMPT 4.13): a change
/// to an entity, an index, a module list in platform.yaml (the catalog seeds it) without its migration fails here, not in production.
/// This does not need a database.
/// </summary>
[Trait("service", "migrator")]
public sealed class MigrationsConsistencyTests
{
    public static TheoryData<string, string> Targets()
    {
        var data = new TheoryData<string, string>();
        foreach (var provider in new[] { SqlServerDatabaseProvider.ProviderName, PostgreSqlDatabaseProvider.ProviderName })
        {
            foreach (var target in MigrationTargets.Catalog.Concat(MigrationTargets.Tenant))
                data.Add(provider, target.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void The_model_has_no_changes_without_a_migration(string provider, string targetName)
    {
        var target = MigrationTargets.Catalog.Concat(MigrationTargets.Tenant).First(t => t.Name == targetName);
        var connection = provider == SqlServerDatabaseProvider.ProviderName ? "Server=design-time;Database=design-time" : "Host=design-time;Database=design-time";

        using var db = target.Create(Databases.Provider(provider), connection);

        db.Database.HasPendingModelChanges().ShouldBeFalse($"{targetName} on {provider} has model changes without a migration. Run: dotnet ef migrations add <Name> (see docs/adding-a-module.md).");
    }
}
