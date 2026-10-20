using Farm360.Core.Data;
using Farm360.MasterData.Domain;
using Microsoft.EntityFrameworkCore;

namespace Farm360.MasterData.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema. No other module reads these tables.</summary>
public sealed class MasterDataDbContext(DbContextOptions<MasterDataDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(item =>
        {
            item.ToTable("Items");
            item.Property(i => i.Name).HasMaxLength(Item.MaxNameLength);
            item.HasIndex(i => i.Name).IsUniqueAmongNotDeleted();
        });
    }
}
