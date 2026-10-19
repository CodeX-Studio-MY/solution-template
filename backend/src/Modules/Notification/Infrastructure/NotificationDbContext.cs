using Saaskit.Core.Data;
using Saaskit.Notification.Domain;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Notification.Infrastructure;

/// <summary>The notification history, in each tenant's database.</summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : ModuleDbContext(options)
{
    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<UserNotification>(notification =>
        {
            notification.ToTable("Notifications");
            notification.Property(n => n.Kind).HasMaxLength(100);
            notification.Property(n => n.Title).HasMaxLength(UserNotification.MaxTitleLength);
            notification.Property(n => n.Body).HasMaxLength(UserNotification.MaxBodyLength);
            notification.Property(n => n.Link).HasMaxLength(300);
            notification.Ignore(n => n.IsRead);
            notification.HasIndex(n => new { n.RecipientId, n.CreatedOn });
            notification.HasIndex(n => new { n.RecipientId, n.ReadOn });
        });
}
