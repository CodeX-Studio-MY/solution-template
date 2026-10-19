using Saaskit.Core.Data;
using Saaskit.Identity.Infrastructure.OpenIddict;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Identity.Infrastructure;

/// <summary>
/// Identity's data in the catalog database: OpenIddict client registrations (with their tenant), and the authorizations
/// and tokens of platform principals that belong to no tenant (platform services).
/// </summary>
public sealed class IdentityCatalogDbContext(DbContextOptions<IdentityCatalogDbContext> options) : PlatformDbContext(options)
{
    public DbSet<OidcApplication> OidcApplications => Set<OidcApplication>();

    public DbSet<OidcAuthorization> OidcAuthorizations => Set<OidcAuthorization>();

    public DbSet<OidcToken> OidcTokens => Set<OidcToken>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        OidcModel.ConfigureApplications(modelBuilder);
        OidcModel.ConfigureAuthorizationsAndTokens(modelBuilder);
    }
}
