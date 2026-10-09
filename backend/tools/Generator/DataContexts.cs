namespace Acme.Generator;

/// <summary>A DbContext the Migrator applies, derived from platform.yaml's services and their databases.</summary>
/// <param name="Target">The migration target name: <c>catalog</c> for the tenant directory, otherwise the service key.</param>
/// <param name="Database"><c>catalog</c> or <c>tenant</c>.</param>
/// <param name="ContextType">The DbContext's full type name.</param>
/// <param name="SchemaExpression">C# expression for the context's schema.</param>
/// <param name="Project">The project containing the context, relative to backend/src (without extension).</param>
public sealed record DataContext(string Target, string Database, string ContextType, string SchemaExpression, string Project)
{
    public string ContextName => ContextType[(ContextType.LastIndexOf('.') + 1)..];

    /// <summary>The migrations project for one provider: <c>{Project}.Migrations.{Suffix}</c>.</summary>
    public string MigrationsProject(string providerSuffix) => $"{Project}.Migrations.{providerSuffix}";
}

public static class DataContexts
{
    public static IReadOnlyList<DataContext> From(PlatformDefinition platform)
    {
        var product = platform.ProductPascal;
        var contexts = new List<DataContext>
        {
            new("catalog", "catalog", $"{product}.Core.Catalog.CatalogDbContext", $"{product}.Core.Catalog.CatalogDbContext.SchemaName", "Core.Catalog/Core.Catalog"),
        };
        foreach (var service in platform.Services)
        {
            var project = $"Modules/{service.Pascal}/{service.Pascal}";
            var schema = $"{product}.Core.Platform.PlatformServices.{service.Pascal}.Schema";
            if (service.UsesCatalogDatabase)
                contexts.Add(new(service.Key, "catalog", $"{product}.{service.Pascal}.Infrastructure.{service.Pascal}CatalogDbContext", schema, project));
            if (service.UsesTenantDatabase)
                contexts.Add(new(service.Key, "tenant", $"{product}.{service.Pascal}.Infrastructure.{service.Pascal}DbContext", schema, project));
        }
        return contexts;
    }

    public static IReadOnlyList<Provider> Providers(PlatformDefinition platform) =>
        platform.Database.Providers.Select(Provider.For).ToList();
}

public sealed record Provider(string Name, string Suffix, string ProviderType, string DesignTimeConnectionString)
{
    public static Provider For(string name) => name switch
    {
        "sqlserver" => new(name, "SqlServer", "Core.Data.SqlServer.SqlServerDatabaseProvider", "Server=design-time;Database=design-time"),
        "postgresql" => new(name, "PostgreSql", "Core.Data.PostgreSql.PostgreSqlDatabaseProvider", "Host=design-time;Database=design-time"),
        _ => throw new PlatformDefinitionException($"Unknown database provider '{name}'."),
    };
}
