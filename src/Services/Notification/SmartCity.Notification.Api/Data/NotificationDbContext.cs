using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Notification.Api.Domain;

namespace SmartCity.Notification.Api.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options) { }

    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSmartCityOutboxEntities();

        modelBuilder.Entity<NotificationRecord>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.Recipient);
            e.HasIndex(n => n.CreatedAt);
            e.Property(n => n.RelatedEntityType).HasConversion<string>();
        });
    }
}
