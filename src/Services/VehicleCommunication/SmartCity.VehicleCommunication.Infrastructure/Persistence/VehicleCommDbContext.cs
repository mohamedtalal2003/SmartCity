using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.VehicleCommunication.Domain.Frames;
using SmartCity.VehicleCommunication.Domain.Vehicles;
using SmartCity.VehicleCommunication.Infrastructure.Persistence.Configurations;

namespace SmartCity.VehicleCommunication.Infrastructure.Persistence;

/// <summary>vehiclecomm_db: vehicles, received_frames, processed_idempotency_keys, and the MassTransit inbox/outbox tables.</summary>
public class VehicleCommDbContext : DbContext
{
    public VehicleCommDbContext(DbContextOptions<VehicleCommDbContext> options) : base(options) { }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ReceivedFrame> ReceivedFrames => Set<ReceivedFrame>();
    public DbSet<ProcessedIdempotencyKey> ProcessedIdempotencyKeys => Set<ProcessedIdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VehicleConfiguration());
        modelBuilder.ApplyConfiguration(new ReceivedFrameConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedIdempotencyKeyConfiguration());

        modelBuilder.AddSmartCityOutboxEntities();
    }
}
