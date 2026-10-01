using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;

namespace SmartCity.Building.Api.Data;

public class BuildingDbContext : DbContext
{
    public BuildingDbContext(DbContextOptions<BuildingDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddSmartCityOutboxEntities();
    }
}
