using Acme.Core.Data;
using Acme.CostCenterBudget.Domain;
using Microsoft.EntityFrameworkCore;

namespace Acme.CostCenterBudget.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema.</summary>
public sealed class CostCenterBudgetDbContext(DbContextOptions<CostCenterBudgetDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<BudgetUsage> Usages => Set<BudgetUsage>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Budget>(budget =>
        {
            budget.ToTable("Budgets");
            budget.Property(b => b.Code).HasMaxLength(Budget.MaxCodeLength);
            budget.HasIndex(b => b.Code).IsUnique();
            budget.Property(b => b.Name).HasMaxLength(Budget.MaxNameLength);
            budget.Property(b => b.Currency).HasMaxLength(3).IsFixedLength();
            budget.Ignore(b => b.Remaining);
        });

        modelBuilder.Entity<BudgetUsage>(usage =>
        {
            usage.ToTable("Usages");
            usage.HasIndex(u => u.RequestId).IsUnique();
            usage.Property(u => u.CostCenter).HasMaxLength(Budget.MaxCodeLength);
            usage.Property(u => u.RequestNumber).HasMaxLength(60);
        });
    }
}
