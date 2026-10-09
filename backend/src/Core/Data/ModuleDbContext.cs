using Microsoft.EntityFrameworkCore;

namespace Acme.Core.Data;

/// <summary>
/// The base of a module's DbContext in the tenant database. A module owns one schema and one context;
/// no module maps another module's tables, and there are no cross-module foreign keys.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : PlatformDbContext(options)
{
    /// <summary>Per-module document counters, updated atomically by <see cref="ISequenceService"/>.</summary>
    public DbSet<SequenceCounter> SequenceCounters => Set<SequenceCounter>();

    private protected override void ConfigurePlatformModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SequenceCounter>(counter =>
        {
            counter.ToTable("SequenceCounters");
            counter.HasKey(c => c.Name);
            counter.Property(c => c.Name).HasMaxLength(SequenceCounter.MaxNameLength);
        });
    }
}

/// <summary>A per-tenant counter row. Never read and increment it in code; use <see cref="ISequenceService"/>.</summary>
public sealed class SequenceCounter
{
    public const int MaxNameLength = 100;

    public string Name { get; private set; } = "";

    public long Value { get; private set; }
}
