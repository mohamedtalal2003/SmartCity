using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartCity.Pothole.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ProcessedIdempotencyKey"/> to the <c>processed_idempotency_keys</c> table.</summary>
public sealed class ProcessedIdempotencyKeyConfiguration : IEntityTypeConfiguration<ProcessedIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<ProcessedIdempotencyKey> k)
    {
        k.ToTable("processed_idempotency_keys");

        k.HasKey(x => x.Key);
        k.Property(x => x.Key).HasColumnName("key").HasMaxLength(64);
        k.Property(x => x.Success).HasColumnName("success");
        k.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(30);
        k.Property(x => x.Message).HasColumnName("message").HasMaxLength(500);
        k.Property(x => x.EntityId).HasColumnName("entity_id");
        k.Property(x => x.Created).HasColumnName("created");
        k.Property(x => x.ProcessedAt).HasColumnName("processed_at");
    }
}
