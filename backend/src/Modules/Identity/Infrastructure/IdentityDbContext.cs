using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.Identity.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
