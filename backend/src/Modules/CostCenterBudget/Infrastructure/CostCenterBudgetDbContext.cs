using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.CostCenterBudget.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema.</summary>
public sealed class CostCenterBudgetDbContext(DbContextOptions<CostCenterBudgetDbContext> options) : ModuleDbContext(options)
{
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
