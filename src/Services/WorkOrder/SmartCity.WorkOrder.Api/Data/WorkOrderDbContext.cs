using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.WorkOrder.Api.Domain;
using SmartCity.WorkOrder.Domain;

namespace SmartCity.WorkOrder.Api.Data;

public class WorkOrderDbContext : DbContext
{
    public WorkOrderDbContext(DbContextOptions<WorkOrderDbContext> options) : base(options) { }

    public DbSet<WorkOrderAggregate> WorkOrders => Set<WorkOrderAggregate>();
    public DbSet<StatusHistoryEntry> StatusHistory => Set<StatusHistoryEntry>();
    public DbSet<PendingCostEstimate> PendingCostEstimates => Set<PendingCostEstimate>();
    public DbSet<ProcessedIdempotencyKey> ProcessedIdempotencyKeys => Set<ProcessedIdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSmartCityOutboxEntities();

        modelBuilder.Entity<WorkOrderAggregate>(e =>
        {
            e.ToTable("work_orders");
            e.HasKey(w => w.Id);
            e.HasIndex(w => w.SourceEntityId).IsUnique();
            e.HasIndex(w => w.TicketNumber).IsUnique();
            e.Property(w => w.Status).HasConversion<string>();
            e.Property(w => w.Priority).HasConversion<string>();
            e.Property(w => w.District).HasConversion<string>();
            e.Property(w => w.Category).HasConversion<string>();
            e.Property(w => w.SourceEntityType).HasConversion<string>();
            e.Property(w => w.CurrentPauseReason).HasConversion<string>();
            e.Property(w => w.EstimatedCost).HasColumnType("numeric(12,2)");
            e.OwnsOne(w => w.Location, loc =>
            {
                loc.Property(l => l.Latitude).HasColumnName("Latitude");
                loc.Property(l => l.Longitude).HasColumnName("Longitude");
                loc.Property(l => l.Altitude).HasColumnName("Altitude");
                loc.Property(l => l.AccuracyMeters).HasColumnName("AccuracyMeters");
                loc.Property(l => l.Heading).HasColumnName("Heading");
            });

            e.HasMany(w => w.StatusHistory)
                .WithOne()
                .HasForeignKey("WorkOrderId")
                .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(w => w.DomainEvents);
        });

        modelBuilder.Entity<StatusHistoryEntry>(e =>
        {
            e.ToTable("work_order_status_history");
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedNever();
            e.Property(h => h.From).HasConversion<string>();
            e.Property(h => h.To).HasConversion<string>();
        });

        modelBuilder.Entity<PendingCostEstimate>(e =>
        {
            e.ToTable("pending_cost_estimates");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.SourceEntityId).IsUnique();
            e.Property(p => p.TotalEstimatedCost).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<ProcessedIdempotencyKey>(e =>
        {
            e.ToTable("processed_idempotency_keys");
            e.HasKey(k => k.Key);
            e.Property(k => k.Key).HasMaxLength(64);
        });
    }
}
