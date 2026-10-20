using Farm360.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Farm360.Core.Data;

/// <summary>
/// The base of a module's DbContext in the tenant database. A module owns one schema and one context;
/// no module maps another module's tables, and there are no cross-module foreign keys.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : PlatformDbContext(options)
{
    /// <summary>Per-module document counters, updated atomically by <see cref="ISequenceService"/>.</summary>
    public DbSet<SequenceCounter> SequenceCounters => Set<SequenceCounter>();

    /// <summary>Integration events waiting to be published (the transactional outbox).</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Messages already handled by this module's consumers (idempotent handling).</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    private protected override void ConfigurePlatformModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("Outbox");
            outbox.HasKey(o => o.Id);
            outbox.Property(o => o.Name).HasMaxLength(100);
            outbox.Property(o => o.TraceParent).HasMaxLength(100);
            outbox.HasIndex(o => o.EventId).IsUnique();
            outbox.HasIndex(o => o.PublishedOn);
        });

        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("Inbox");
            inbox.HasKey(i => new { i.EventId, i.Consumer });
            inbox.Property(i => i.Consumer).HasMaxLength(InboxMessage.MaxConsumerLength);
        });

        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.ToTable("IdempotencyRecords");
            record.HasKey(r => new { r.PrincipalId, r.Key });
            record.Property(r => r.PrincipalId).HasMaxLength(100);
            record.Property(r => r.Key).HasMaxLength(IdempotencyRecord.MaxKeyLength);
            record.Property(r => r.RequestHash).HasMaxLength(100);
            record.Property(r => r.ContentType).HasMaxLength(100);
            record.Property(r => r.Location).HasMaxLength(500);
            record.HasIndex(r => r.CreatedOn);
        });

        modelBuilder.Entity<StoredFile>(file =>
        {
            file.ToTable("StoredFiles");
            file.Property(f => f.Key).HasMaxLength(StoredFile.MaxKeyLength);
            file.HasIndex(f => f.Key).IsUnique();
            file.Property(f => f.ContentType).HasMaxLength(100);
            file.Property(f => f.Sha256).HasMaxLength(64);
            file.Property(f => f.DisplayName).HasMaxLength(200);
            file.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
        });

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
