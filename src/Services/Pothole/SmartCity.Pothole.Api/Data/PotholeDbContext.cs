using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Pothole.Api.Domain;

namespace SmartCity.Pothole.Api.Data;

public class PotholeDbContext : DbContext
{
    public PotholeDbContext(DbContextOptions<PotholeDbContext> options) : base(options) { }

    public DbSet<PotholeRecord> Potholes => Set<PotholeRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSmartCityOutboxEntities();

        modelBuilder.Entity<PotholeRecord>(e =>
        {
            e.ToTable("potholes");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.SourceDetectionId).IsUnique();
            e.Property(p => p.Location).HasColumnType("geography(Point,4326)");
            e.HasIndex(p => p.Location).HasMethod("gist");
            e.Property(p => p.District).HasConversion<string>();
            e.Property(p => p.DetectionType).HasConversion<string>();
            e.Property(p => p.Stage).HasConversion<string>();
            e.Property(p => p.Severity).HasConversion<string>();
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.ImageUrls).HasColumnType("text[]");
        });
    }
}
