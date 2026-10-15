using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartCity.Pothole.Infrastructure.Persistence.Configurations;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>Maps <see cref="Observation"/> to the <c>observations</c> table.</summary>
public sealed class ObservationConfiguration : IEntityTypeConfiguration<Observation>
{
    public void Configure(EntityTypeBuilder<Observation> o)
    {
        o.ToTable("observations");

        o.HasKey(x => x.Id);
        // The id is set in code, so EF inserts a new observation found in the pothole's list.
        o.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        o.Property(x => x.PotholeId).HasColumnName("pothole_id");
        o.Property(x => x.SourceDetectionId).HasColumnName("source_detection_id");
        o.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(30);
        o.Property(x => x.DamageType).HasColumnName("damage_type").HasConversion<string>().HasMaxLength(30);
        o.Property(x => x.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(20);
        o.Property(x => x.Stage).HasColumnName("stage").HasConversion<string>().HasMaxLength(30);
        o.Property(x => x.AreaM2).HasColumnName("area_m2");
        o.Property(x => x.DepthCm).HasColumnName("depth_cm");
        o.Property(x => x.Confidence).HasColumnName("confidence");
        o.Property(x => x.VerificationResult).HasColumnName("verification_result").HasMaxLength(20);
        o.Property(x => x.FrameId).HasColumnName("frame_id");
        o.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(1000);
        o.Property(x => x.Notes).HasColumnName("notes");
        o.Property(x => x.SeenAt).HasColumnName("seen_at");
        o.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(100);

        // The same AI detection is never saved twice.
        o.HasIndex(x => x.SourceDetectionId, "ux_observations_source_detection_id")
            .IsUnique()
            .HasFilter("source_detection_id IS NOT NULL");
        // The same verification frame is saved once per pothole.
        o.HasIndex(x => new { x.PotholeId, x.FrameId }, "ux_observations_pothole_frame_id")
            .IsUnique()
            .HasFilter("frame_id IS NOT NULL");
        // The pothole history, newest first.
        o.HasIndex(x => new { x.PotholeId, x.SeenAt }, "ix_observations_pothole_seen_at")
            .IsDescending(false, true);
    }
}
