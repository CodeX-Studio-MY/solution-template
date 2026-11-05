using Saaskit.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Base.Infrastructure;

/// <summary>
/// The module's data in each tenant's database, in its own schema. No other module reads these tables. It ships with no business tables:
/// a product's master tables (<c>MsFarm</c>, <c>MsCrop</c>, ...) are added here, see docs/adding-a-master-entity.md.
/// </summary>
public sealed class BaseDbContext(DbContextOptions<BaseDbContext> options) : ModuleDbContext(options)
{
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
