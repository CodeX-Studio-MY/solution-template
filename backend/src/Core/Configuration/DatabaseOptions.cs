using System.ComponentModel.DataAnnotations;

namespace Acme.Core.Configuration;

[OptionsSection("Database", "The catalog database, tenant database servers and the database provider.")]
public sealed record DatabaseOptions
{
    [Required, AllowedValues("sqlserver", "postgresql")]
    [Setting("Database provider for the catalog and every tenant database.", Example = "sqlserver")]
    public string Provider { get; init; } = "";

    [Required, Secret]
    [Setting("Connection string for the catalog database. Use managed identity (no password) in the cloud.")]
    public string CatalogConnectionString { get; init; } = "";

    /// <summary>Base connection strings per server or pool name. A tenant's placement names the pool and its database.</summary>
    [Secret]
    [Setting("Base connection string for a tenant server or pool, without a database name. One entry per pool; 'default' is the pool new tenants use.")]
    public Dictionary<string, string> Servers { get; init; } = [];

    [Range(1, 1000)]
    [Setting("Max Pool Size for each tenant connection string. Ceiling: tenants x instances x this value.")]
    public int MaxPoolSize { get; init; } = 50;

    [Range(1, 3600)]
    [Setting("How long a tenant's resolved connection is cached, in seconds.")]
    public int TenantCacheSeconds { get; init; } = 30;

    [Range(1, 64)]
    [Setting("How many tenants the Migrator migrates in parallel with --all.")]
    public int MigrationParallelism { get; init; } = 4;
}
