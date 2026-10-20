using Farm360.Core.Data;
using Farm360.MasterData.Domain;
using Microsoft.EntityFrameworkCore;

namespace Farm360.MasterData.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema. No other module reads these tables.</summary>
public sealed class MasterDataDbContext(DbContextOptions<MasterDataDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Farm> Farms => Set<Farm>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Farm>(farm =>
        {
            farm.ToTable("Farms");
            farm.Property(f => f.Name).HasMaxLength(Farm.MaxNameLength);
            farm.Property(f => f.Code).HasMaxLength(Farm.MaxCodeLength);
            farm.Property(f => f.GlobalGapNumber).HasMaxLength(Farm.MaxGlobalGapNumberLength);
            farm.Property(f => f.Description).HasMaxLength(Farm.MaxDescriptionLength);
            farm.Property(f => f.Address).HasMaxLength(Farm.MaxAddressLength);
            farm.Property(f => f.ContactPerson).HasMaxLength(Farm.MaxContactPersonLength);
            farm.Property(f => f.ContactPhone).HasMaxLength(Farm.MaxContactPhoneLength);
            // Location: geography (SRID 4326) on every provider, by the platform's spatial conventions.

            // A code, when set, is unique among the farms that are not deleted.
            farm.HasIndex(f => f.Code).IsUniqueAmongNotDeleted().IsUniqueWhereNotNull(nameof(Farm.Code));
            farm.HasIndex(f => f.Name);
        });
    }
}
