using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Pothole.Infrastructure.Persistence.Configurations;
using SmartCity.Pothole.Infrastructure.RecurringSpots;

namespace SmartCity.Pothole.Infrastructure.Persistence;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// pothole_db: potholes, observations, admin_areas, processed_idempotency_keys, recurring_spots, and the
/// MassTransit inbox/outbox tables.
/// </summary>
public class PotholeDbContext : DbContext
{
    public PotholeDbContext(DbContextOptions<PotholeDbContext> options) : base(options) { }

    public DbSet<Pothole> Potholes => Set<Pothole>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<AdminArea> AdminAreas => Set<AdminArea>();
    public DbSet<ProcessedIdempotencyKey> ProcessedIdempotencyKeys => Set<ProcessedIdempotencyKey>();
    public DbSet<RecurringSpot> RecurringSpots => Set<RecurringSpot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.ApplyConfiguration(new PotholeConfiguration());
        modelBuilder.ApplyConfiguration(new ObservationConfiguration());
        modelBuilder.ApplyConfiguration(new AdminAreaConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedIdempotencyKeyConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringSpotConfiguration());

        modelBuilder.AddSmartCityOutboxEntities();
    }
}
