using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCity.Pothole.Infrastructure.RecurringSpots;

namespace SmartCity.Pothole.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="RecurringSpot"/> to the <c>recurring_spots</c> table (see the Pothole Service Data page).</summary>
public sealed class RecurringSpotConfiguration : IEntityTypeConfiguration<RecurringSpot>
{
    public void Configure(EntityTypeBuilder<RecurringSpot> s)
    {
        s.ToTable("recurring_spots");

        s.HasKey(x => x.Id);
        s.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        s.Property(x => x.Location).HasColumnName("location").HasColumnType("geography(Point,4326)");
        // The data page has "district varchar"; districts are ids now (as on potholes), so it is district_id.
        s.Property(x => x.DistrictId).HasColumnName("district_id");
        s.Property(x => x.PotholeIds).HasColumnName("pothole_ids").HasColumnType("uuid[]");
        s.Property(x => x.PotholeCount).HasColumnName("pothole_count");
        s.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
        s.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        s.Property(x => x.ComputedAt).HasColumnName("computed_at");

        s.HasIndex(x => x.Location, "ix_recurring_spots_location").HasMethod("gist");
        s.HasIndex(x => x.DistrictId, "ix_recurring_spots_district_id");
    }
}
