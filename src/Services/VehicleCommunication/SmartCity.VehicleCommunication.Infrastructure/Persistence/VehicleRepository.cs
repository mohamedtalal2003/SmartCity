using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCity.Contracts.Enums;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Commands;
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

    public void Add(Vehicle vehicle) => _db.Vehicles.Add(vehicle);

    public async Task<IReadOnlyList<string>> ListActiveVehicleIdsAsync(CancellationToken ct) =>
        await _db.Vehicles.AsNoTracking()
            .Where(v => v.Status == VehicleStatus.Active)
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Vehicle>> ListOnlineNotSeenSinceAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        await _db.Vehicles
            .Where(v => v.ConnectionStatus == VehicleConnectionStatus.Online && (v.LastSeenAt == null || v.LastSeenAt < cutoff))
            .ToListAsync(ct);

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

    public async Task<CommandResult?> FindCommandResultAsync(string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(idempotencyKey))
            return null;

        var row = await _db.ProcessedIdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Key == idempotencyKey, ct);
        return row is null
            ? null
            : new CommandResult(row.Success, Enum.Parse<CommandError>(row.ErrorCode), row.Message, row.EntityId, row.Created, row.ProcessedAt);
    }

    public void RememberCommandResult(string idempotencyKey, CommandResult result)
    {
        // Like Pothole: no key, nothing to remember; only successful results are kept.
        if (string.IsNullOrEmpty(idempotencyKey) || !result.Success)
            return;

        _db.ProcessedIdempotencyKeys.Add(new ProcessedIdempotencyKey
        {
            Key = idempotencyKey,
            Success = result.Success,
            ErrorCode = result.Error.ToString(),
            Message = result.Message,
            EntityId = result.EntityId,
            Created = result.Created,
            ProcessedAt = result.ProcessedAt
        });
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
