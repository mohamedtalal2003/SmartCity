using SmartCity.VehicleCommunication.Application.Commands;
using SmartCity.VehicleCommunication.Domain.Frames;
using SmartCity.VehicleCommunication.Domain.Vehicles;

namespace SmartCity.VehicleCommunication.Application.Abstractions;

/// <summary>
/// The only data port of the Vehicle Communication use cases. One named method for each thing a use case
/// needs. Implemented in Infrastructure with EF Core.
/// </summary>
public interface IVehicleRepository
{
    Task<Vehicle?> GetAsync(string vehicleId, CancellationToken ct);

    void Add(Vehicle vehicle);

    /// <summary>True when a frame with this id was already saved.</summary>
    Task<bool> ReceivedFrameExistsAsync(Guid frameId, CancellationToken ct);

    /// <summary>
    /// Adds the frame row and saves everything in one transaction, together with the events in the outbox.
    /// Returns false (and saves nothing) when another call saved the same frame id first.
    /// </summary>
    Task<bool> SaveNewFrameAsync(ReceivedFrame frame, CancellationToken ct);

    /// <summary>The result of an earlier command with this idempotency key, or null.</summary>
    Task<CommandResult?> FindCommandResultAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>Keeps a successful command result with its key. Saved by the same <see cref="SaveChangesAsync"/>.</summary>
    void RememberCommandResult(string idempotencyKey, CommandResult result);

    /// <summary>Saves everything in one transaction.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
