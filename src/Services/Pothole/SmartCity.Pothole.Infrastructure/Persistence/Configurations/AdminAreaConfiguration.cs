using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartCity.Pothole.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AdminArea"/> to the <c>admin_areas</c> table.</summary>
public sealed class AdminAreaConfiguration : IEntityTypeConfiguration<AdminArea>
{
    public void Configure(EntityTypeBuilder<AdminArea> a)
    {
        a.ToTable("admin_areas");

        a.HasKey(x => x.Id);
        a.Property(x => x.Id).HasColumnName("id");
        a.Property(x => x.OsmId).HasColumnName("osm_id");
        a.Property(x => x.Name).HasColumnName("name");
        a.Property(x => x.Level).HasColumnName("level").HasConversion<string>().HasMaxLength(20);
        a.Property(x => x.ParentId).HasColumnName("parent_id");
        a.Property(x => x.Geometry).HasColumnName("geometry").HasColumnType("geometry(MultiPolygon,4326)");

        a.HasOne<AdminArea>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        // Find the district and neighborhood of a point.
        a.HasIndex(x => x.Geometry, "ix_admin_areas_geometry").HasMethod("gist");
    }
}
