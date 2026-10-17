using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartCity.VehicleCommunication.Domain.Vehicles;

namespace SmartCity.VehicleCommunication.Infrastructure.Persistence;

/// <summary>
/// Dev only: adds the simulator's vehicles MUN-VEH-001 … MUN-VEH-005 (Active) when they are missing.
/// Runs when the config <c>Seed:DevVehicles</c> is true (appsettings.Development.json and docker-compose).
/// </summary>
public sealed class DevVehicleSeeder
{
    private const int Count = 5;

    private readonly VehicleCommDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<DevVehicleSeeder> _logger;

    public DevVehicleSeeder(VehicleCommDbContext db, TimeProvider time, ILogger<DevVehicleSeeder> logger)
    {
        _db = db;
        _time = time;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await _db.Vehicles.Select(v => v.Id).ToListAsync(ct);
        var now = _time.GetUtcNow();
        var added = 0;

        for (var i = 1; i <= Count; i++)
        {
            var id = $"{Vehicle.IdPrefix}{i:D3}";
            if (existing.Contains(id))
                continue;

            // Same plates as tools/VehicleSimulator.
            _db.Vehicles.Add(Vehicle.Register(id, $"34 ABC {i:D3}", districtId: null, notes: "dev seed", now));
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} dev vehicles", added);
        }
    }
}
