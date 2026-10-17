using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCity.VehicleCommunication.Domain.Vehicles;

namespace SmartCity.VehicleCommunication.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Vehicle"/> to the <c>vehicles</c> table (see the tables on the design page).</summary>
public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> v)
    {
        v.ToTable("vehicles");

        v.HasKey(x => x.Id);
        v.Property(x => x.Id).HasColumnName("id").HasMaxLength(20).ValueGeneratedNever();
        v.Property(x => x.Plate).HasColumnName("plate").HasMaxLength(Vehicle.MaxPlateLength);
        v.Property(x => x.DistrictId).HasColumnName("district_id");
        v.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(Vehicle.MaxNotesLength);
        v.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);

        v.Property(x => x.ConnectionStatus).HasColumnName("connection_status").HasConversion<string>().HasMaxLength(20);
        v.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        v.Property(x => x.LastLatitude).HasColumnName("last_latitude");
        v.Property(x => x.LastLongitude).HasColumnName("last_longitude");
        v.Property(x => x.PendingFrames).HasColumnName("pending_frames");

        v.Property(x => x.CreatedAt).HasColumnName("created_at");
        v.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // The domain increases RowVersion on every change. If another writer saved first, SaveChanges
        // throws DbUpdateConcurrencyException and nothing is written.
        v.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        // The offline sweep looks for Online vehicles that have not been seen for a while.
        v.HasIndex(x => new { x.ConnectionStatus, x.LastSeenAt });
    }
}
