using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Pothole.Infrastructure.Persistence.Configurations;

namespace SmartCity.Pothole.Infrastructure.Persistence;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>pothole_db: potholes, observations, admin_areas, and the MassTransit inbox/outbox tables.</summary>
public class PotholeDbContext : DbContext
{
    public PotholeDbContext(DbContextOptions<PotholeDbContext> options) : base(options) { }

    public DbSet<Pothole> Potholes => Set<Pothole>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<AdminArea> AdminAreas => Set<AdminArea>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.ApplyConfiguration(new PotholeConfiguration());
        modelBuilder.ApplyConfiguration(new ObservationConfiguration());
        modelBuilder.ApplyConfiguration(new AdminAreaConfiguration());

        modelBuilder.AddSmartCityOutboxEntities();
    }
}
