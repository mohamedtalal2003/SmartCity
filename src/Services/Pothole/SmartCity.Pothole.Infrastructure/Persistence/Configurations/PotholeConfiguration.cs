using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetTopologySuite.Geometries;

namespace SmartCity.Pothole.Infrastructure.Persistence.Configurations;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>Maps <see cref="Pothole"/> to the <c>potholes</c> table (see the Pothole Service Data page).</summary>
public sealed class PotholeConfiguration : IEntityTypeConfiguration<Pothole>
{
    public void Configure(EntityTypeBuilder<Pothole> p)
    {
        p.ToTable("potholes", t =>
        {
            // A closed pothole always has a reason and a time; an open one has neither.
            t.HasCheckConstraint("ck_potholes_closed_has_reason",
                "(status = 'Closed' AND closed_reason IS NOT NULL AND closed_at IS NOT NULL) OR " +
                "(status = 'Open' AND closed_reason IS NULL AND closed_at IS NULL)");
            // A duplicate points to another pothole, never to itself.
            t.HasCheckConstraint("ck_potholes_duplicate_has_other_id",
                "closed_reason IS DISTINCT FROM 'Duplicate' OR (duplicate_of_id IS NOT NULL AND duplicate_of_id <> id)");
        });

        p.HasKey(x => x.Id);
        p.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        // GeoPoint in the domain, a PostGIS geography point in the database.
        p.Property(x => x.Location)
            .HasColumnName("location")
            .HasColumnType("geography(Point,4326)")
            .HasConversion(
                point => new Point(point.Longitude, point.Latitude) { SRID = 4326 },
                column => new GeoPoint(column.Y, column.X));

        p.Property(x => x.DistrictId).HasColumnName("district_id");
        p.Property(x => x.NeighborhoodId).HasColumnName("neighborhood_id");
        p.Property(x => x.RoadName).HasColumnName("road_name").HasMaxLength(200);
        p.Property(x => x.DamageType).HasColumnName("damage_type").HasConversion<string>().HasMaxLength(30);

        p.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        p.Property(x => x.ClosedReason).HasColumnName("closed_reason").HasConversion<string>().HasMaxLength(30);
        p.Property(x => x.ClosedAt).HasColumnName("closed_at");
        p.Property(x => x.DuplicateOfId).HasColumnName("duplicate_of_id");

        p.Property(x => x.CurrentSeverity).HasColumnName("current_severity").HasConversion<string>().HasMaxLength(20);
        p.Property(x => x.CurrentStage).HasColumnName("current_stage").HasConversion<string>().HasMaxLength(30);
        p.Property(x => x.AreaM2).HasColumnName("area_m2");
        p.Property(x => x.DepthCm).HasColumnName("depth_cm");
        p.Property(x => x.Confidence).HasColumnName("confidence");
        p.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20);
        p.Property(x => x.SightingsCount).HasColumnName("sightings_count");

        p.OwnsOne(x => x.Estimate, e =>
        {
            e.Property(c => c.Materials).HasColumnName("est_materials").HasPrecision(12, 2);
            e.Property(c => c.Labor).HasColumnName("est_labor").HasPrecision(12, 2);
            e.Property(c => c.Equipment).HasColumnName("est_equipment").HasPrecision(12, 2);
            e.Property(c => c.Total).HasColumnName("est_total").HasPrecision(12, 2);
            e.Property(c => c.Currency).HasColumnName("est_currency").HasMaxLength(3).IsFixedLength();

            // Only the money is stored (the list filters on est_total). Hours, crew size, priority and
            // material are computed fresh by PotholeEstimator whenever PotholeSaved is built.
            e.Ignore(c => c.LaborHours);
            e.Ignore(c => c.CrewSize);
            e.Ignore(c => c.SuggestedPriority);
            e.Ignore(c => c.Material);
        });
        p.Navigation(x => x.Estimate).IsRequired();

        p.Property(x => x.WorkOrderId).HasColumnName("work_order_id");
        p.Property(x => x.RepairStatus).HasColumnName("repair_status").HasMaxLength(30);
        p.Property(x => x.RepairStatusSeq).HasColumnName("repair_status_seq");

        p.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
        p.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");

        // The domain increases RowVersion on every change. If another writer saved first, SaveChanges
        // throws and MassTransit retries the message.
        p.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        // Observations are only added. They are inserted through this list but never loaded with a command.
        p.HasMany(x => x.Observations).WithOne().HasForeignKey(o => o.PotholeId).OnDelete(DeleteBehavior.Restrict);
        p.Navigation(x => x.Observations).HasField("_observations").UsePropertyAccessMode(PropertyAccessMode.Field);

        p.HasOne<Pothole>().WithMany().HasForeignKey(x => x.DuplicateOfId).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<AdminArea>().WithMany().HasForeignKey(x => x.DistrictId).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<AdminArea>().WithMany().HasForeignKey(x => x.NeighborhoodId).OnDelete(DeleteBehavior.Restrict);

        // Fast matching: "is there an open pothole within 3 m?"
        p.HasIndex(x => x.Location, "ix_potholes_location_open").HasMethod("gist").HasFilter("status = 'Open'");
        // The 30-day rule: "was a pothole near here closed as a mistake?"
        p.HasIndex(x => x.Location, "ix_potholes_location_false_positive").HasMethod("gist")
            .HasFilter("closed_reason = 'FalsePositive'");
        p.HasIndex(x => new { x.DistrictId, x.Status }, "ix_potholes_district_status");
    }
}
