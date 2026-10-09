using Acme.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acme.Notification.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema.</summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : ModuleDbContext(options)
{
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
