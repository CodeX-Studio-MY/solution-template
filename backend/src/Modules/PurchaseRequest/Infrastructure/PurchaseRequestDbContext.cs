using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.PurchaseRequest.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema.</summary>
public sealed class PurchaseRequestDbContext(DbContextOptions<PurchaseRequestDbContext> options) : ModuleDbContext(options)
{
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
