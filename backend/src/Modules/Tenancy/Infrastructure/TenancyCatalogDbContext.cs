using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.Tenancy.Infrastructure;

/// <summary>The module's data in the catalog database, in its own schema.</summary>
public sealed class TenancyCatalogDbContext(DbContextOptions<TenancyCatalogDbContext> options) : PlatformDbContext(options)
{
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
