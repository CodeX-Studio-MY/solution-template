using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Farm360.Core.Data;

/// <summary>
/// Everything that differs between database providers. Business code never sees it:
/// implementations live only in the <c>Core.Data.*</c> provider packages.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>The provider name used in configuration and tenant placement, for example <c>sqlserver</c>.</summary>
    string Name { get; }

    /// <summary>Suffix of each module's migrations assembly for this provider, for example <c>SqlServer</c>.</summary>
    string MigrationsAssemblySuffix { get; }

    /// <summary>Builds a tenant connection string from a pool's base connection string.</summary>
    string BuildConnectionString(string baseConnectionString, string databaseName, int maxPoolSize);

    /// <summary>Configures EF Core for a module context: provider, schema-local migrations history, migrations assembly.</summary>
    void Configure(DbContextOptionsBuilder options, string connectionString, string schema, string migrationsAssembly);

    /// <summary>Provider-specific model conventions (key generation, naming, timestamps).</summary>
    void ConfigureConventions(ModelConfigurationBuilder configuration);

    /// <summary>Configures the provider's row version as the entity's concurrency token (a shadow property).</summary>
    void ConfigureConcurrencyToken(EntityTypeBuilder entity);

    /// <summary>The current concurrency token of a tracked entity, encoded for use as an ETag.</summary>
    string? GetConcurrencyToken(EntityEntry entry);

    /// <summary>Sets the concurrency token the client last saw, so EF Core detects a conflicting update.</summary>
    void SetOriginalConcurrencyToken(EntityEntry entry, string token);

    /// <summary>The native JSON column type (<c>json</c> on SQL Server 2025 and Azure SQL, <c>jsonb</c> on PostgreSQL).</summary>
    string JsonColumnType { get; }

    /// <summary>SQL for a filtered unique index that ignores soft-deleted rows.</summary>
    string NotDeletedFilter { get; }

    /// <summary>SQL filter for a nullable column that is set, used by filtered unique indexes.</summary>
    string NotNullFilter(string propertyName);

    /// <summary>Creates the database on the server of <paramref name="serverConnectionString"/> if it doesn't exist. Used by tenant onboarding.</summary>
    Task EnsureDatabaseAsync(string serverConnectionString, string databaseName, CancellationToken cancellationToken = default);

    /// <summary>Drops the database if it exists, closing its pooled connections first. Used by offboarding after the grace period.</summary>
    Task DropDatabaseAsync(string serverConnectionString, string databaseName, CancellationToken cancellationToken = default);

    /// <summary>SQL for a check constraint that exactly one of two nullable columns is set.</summary>
    string ExactlyOneSetCheck(string firstProperty, string secondProperty);

    /// <summary>SQL for a check constraint that the column matches a regular expression.</summary>
    string RegexCheck(string propertyName, string pattern);

    /// <summary>Registers provider-specific services (sequence service, distributed lock, catalog health check).</summary>
    void AddServices(IServiceCollection services, string catalogConnectionString);
}

/// <summary>The provider chosen for this environment (<c>Database:Provider</c>).</summary>
public interface IDatabaseProviderAccessor
{
    IDatabaseProvider Current { get; }

    IDatabaseProvider ForName(string name);
}
