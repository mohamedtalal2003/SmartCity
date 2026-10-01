using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.CostCalculation.Api.Domain;

namespace SmartCity.CostCalculation.Api.Data;

public class CostCalcDbContext : DbContext
{
    public CostCalcDbContext(DbContextOptions<CostCalcDbContext> options) : base(options) { }

    public DbSet<CostEstimate> CostEstimates => Set<CostEstimate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSmartCityOutboxEntities();

        modelBuilder.Entity<CostEstimate>(e =>
        {
            e.ToTable("cost_estimates");
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.SourceEntityId);
            e.Property(c => c.SourceEntityType).HasConversion<string>();
            e.Property(c => c.Severity).HasConversion<string>();
            e.Property(c => c.SuggestedPriority).HasConversion<string>();
            e.Property(c => c.MaterialCost).HasColumnType("numeric(12,2)");
            e.Property(c => c.LaborCost).HasColumnType("numeric(12,2)");
            e.Property(c => c.EquipmentCost).HasColumnType("numeric(12,2)");
            e.Property(c => c.TotalEstimatedCost).HasColumnType("numeric(12,2)");
        });
    }
}
