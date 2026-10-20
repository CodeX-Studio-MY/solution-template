using Farm360.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Farm360.Scheduler;

/// <summary>
/// Owns the <c>scheduler</c> schema in the catalog database, where Quartz keeps its jobs, triggers and cluster state. It maps no
/// entities: Quartz reads and writes its own tables. Migrations create them (so only the Migrator changes the schema).
/// </summary>
public sealed class SchedulerStoreDbContext(DbContextOptions<SchedulerStoreDbContext> options) : PlatformDbContext(options)
{
    public const string SchemaName = "scheduler";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
