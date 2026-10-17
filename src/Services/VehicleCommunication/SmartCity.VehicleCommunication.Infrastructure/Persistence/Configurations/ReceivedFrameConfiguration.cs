using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCity.VehicleCommunication.Domain.Frames;

namespace SmartCity.VehicleCommunication.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ReceivedFrame"/> to the <c>received_frames</c> table. The frame id is the primary key,
/// which is what stops the same frame being saved twice.
/// </summary>
public sealed class ReceivedFrameConfiguration : IEntityTypeConfiguration<ReceivedFrame>
{
    public const string TableName = "received_frames";

    public void Configure(EntityTypeBuilder<ReceivedFrame> f)
    {
        f.ToTable(TableName);

        f.HasKey(x => x.FrameId);
        f.Property(x => x.FrameId).HasColumnName("frame_id").ValueGeneratedNever();
        f.Property(x => x.VehicleId).HasColumnName("vehicle_id").HasMaxLength(20);
        f.Property(x => x.CapturedAt).HasColumnName("captured_at");
        f.Property(x => x.ReceivedAt).HasColumnName("received_at");
        f.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(200);
        f.Property(x => x.SizeBytes).HasColumnName("size_bytes");

        // "Which frames came from this vehicle, and when."
        f.HasIndex(x => new { x.VehicleId, x.ReceivedAt });
    }
}
