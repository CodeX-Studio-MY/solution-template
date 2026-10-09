namespace Acme.Core.Platform;

/// <summary>
/// A service declared in <c>platform.yaml</c>: something that runs and is routed at <c>/api/{Key}/*</c>.
/// Instances are generated into <see cref="PlatformServices"/>; never construct one by hand.
/// </summary>
/// <param name="Key">The service key (lowercase kebab-case).</param>
/// <param name="Port">The local development port.</param>
/// <param name="Audience">The token audience for this service, <c>{product}:{key}</c>.</param>
/// <param name="OwnedModules">The access modules this service owns.</param>
/// <param name="UsesTenantDatabase">Whether the service keeps data in each tenant's database.</param>
/// <param name="UsesCatalogDatabase">Whether the service keeps data in the catalog database.</param>
public sealed record PlatformService(
    string Key,
    int Port,
    string Audience,
    IReadOnlyList<string> OwnedModules,
    bool UsesTenantDatabase,
    bool UsesCatalogDatabase)
{
    /// <summary>The route prefix every endpoint of this service lives under.</summary>
    public string RoutePrefix => $"/api/{Key}";

    /// <summary>The database schema name for this service's module (hyphens become underscores).</summary>
    public string Schema => Key.Replace('-', '_');
}
