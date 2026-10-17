using Microsoft.Extensions.Logging;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Commands;
using SmartCity.VehicleCommunication.Domain.Vehicles;

namespace SmartCity.VehicleCommunication.Application.Vehicles;

public enum VehicleStatusChange
{
    Suspend,    // V3: stop accepting its frames
    Resume,     // V3: accept them again
    Retire      // V4: out of service for good
}

/// <summary>The admin suspends, resumes or retires a vehicle. The reason is free text for the log.</summary>
public sealed record ChangeVehicleStatusCommand(CommandContext Context, string VehicleId, VehicleStatusChange Change, string? Reason);

/// <summary>
/// Use cases V3 and V4. The next upload from a Suspended or Retired vehicle gets PERMISSION_DENIED; the vehicle
/// keeps its frames on the card until it is resumed or they expire.
/// </summary>
public sealed class ChangeVehicleStatus
{
    private readonly IVehicleRepository _repository;
    private readonly TimeProvider _time;
    private readonly ILogger<ChangeVehicleStatus> _logger;

    public ChangeVehicleStatus(IVehicleRepository repository, TimeProvider time, ILogger<ChangeVehicleStatus> logger)
    {
        _repository = repository;
        _time = time;
        _logger = logger;
    }

    public async Task<CommandResult> HandleAsync(ChangeVehicleStatusCommand command, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        // 1. The same idempotency key again: return the first result.
        var earlier = await _repository.FindCommandResultAsync(command.Context.IdempotencyKey, ct);
        if (earlier is not null)
            return earlier;

        // 2. Load the vehicle.
        var vehicle = await _repository.GetAsync(command.VehicleId, ct);
        if (vehicle is null)
            return CommandResult.Failed(CommandError.NotFound, $"Vehicle {command.VehicleId} not found.", now);

        try
        {
            switch (command.Change)
            {
                case VehicleStatusChange.Suspend: vehicle.Suspend(now); break;
                case VehicleStatusChange.Resume: vehicle.Resume(now); break;
                case VehicleStatusChange.Retire: vehicle.Retire(now); break;
            }

            // SKELETON: no event is published (see AddVehicle).
            var result = CommandResult.Ok(vehicle.Id, created: false, now);
            _repository.RememberCommandResult(command.Context.IdempotencyKey, result);
            await _repository.SaveChangesAsync(ct);

            _logger.LogInformation("Vehicle {VehicleId} is now {Status} ({Change} by {SubmittedBy}: {Reason})",
                vehicle.Id, vehicle.Status, command.Change, command.Context.SubmittedBy, command.Reason ?? "");
            return result;
        }
        catch (DomainException e)
        {
            // E.g. resume an Active vehicle, or change a Retired one.
            return CommandResult.Failed(CommandError.InvalidState, e.Message, now);
        }
    }
}
