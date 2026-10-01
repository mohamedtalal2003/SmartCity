using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;

namespace SmartCity.Violation.Api.Data;

public class ViolationDbContext : DbContext
{
    public ViolationDbContext(DbContextOptions<ViolationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSmartCityOutboxEntities();
    }
}
