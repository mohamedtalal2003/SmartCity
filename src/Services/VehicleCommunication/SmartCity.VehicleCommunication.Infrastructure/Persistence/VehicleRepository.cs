using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Domain.Frames;
using SmartCity.VehicleCommunication.Domain.Vehicles;
using SmartCity.VehicleCommunication.Infrastructure.Persistence.Configurations;

namespace SmartCity.VehicleCommunication.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IVehicleRepository"/>.</summary>
public sealed class VehicleRepository : IVehicleRepository
{
    private readonly VehicleCommDbContext _db;

    public VehicleRepository(VehicleCommDbContext db) => _db = db;

    public Task<Vehicle?> GetAsync(string vehicleId, CancellationToken ct) =>
        _db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, ct);

    public Task<bool> ReceivedFrameExistsAsync(Guid frameId, CancellationToken ct) =>
        _db.ReceivedFrames.AnyAsync(f => f.FrameId == frameId, ct);

    public async Task<bool> SaveNewFrameAsync(ReceivedFrame frame, CancellationToken ct)
    {
        _db.ReceivedFrames.Add(frame);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: ReceivedFrameConfiguration.TableName
        })
        {
            // The transaction rolled back, so the outbox message was not saved either.
            return false;
        }
    }
}
