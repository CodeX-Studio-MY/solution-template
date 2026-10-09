using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.Identity.Infrastructure;

/// <summary>The module's data in the catalog database, in its own schema.</summary>
public sealed class IdentityCatalogDbContext(DbContextOptions<IdentityCatalogDbContext> options) : PlatformDbContext(options)
{
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
