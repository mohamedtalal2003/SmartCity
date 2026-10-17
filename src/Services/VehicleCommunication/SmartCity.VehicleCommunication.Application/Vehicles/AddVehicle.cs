using Microsoft.Extensions.Logging;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Commands;
using SmartCity.VehicleCommunication.Domain.Vehicles;

namespace SmartCity.VehicleCommunication.Application.Vehicles;

/// <summary>The admin adds a vehicle (V1). A district id of null or 0 means none.</summary>
public sealed record AddVehicleCommand(CommandContext Context, string VehicleId, string Plate, int? DistrictId, string? Notes);

/// <summary>
/// Use case V1: the admin adds a vehicle. It starts Active and Offline.
/// Its certificate is made outside the system for now (deferred on the design page).
/// </summary>
public sealed class AddVehicle
{
    private readonly IVehicleRepository _repository;
    private readonly TimeProvider _time;
    private readonly ILogger<AddVehicle> _logger;

    public AddVehicle(IVehicleRepository repository, TimeProvider time, ILogger<AddVehicle> logger)
    {
        _repository = repository;
        _time = time;
        _logger = logger;
    }

    public async Task<CommandResult> HandleAsync(AddVehicleCommand command, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        // 1. The same idempotency key again: return the first result.
        var earlier = await _repository.FindCommandResultAsync(command.Context.IdempotencyKey, ct);
        if (earlier is not null)
            return earlier;

        // 2. The id must be new.
        if (await _repository.GetAsync(command.VehicleId, ct) is not null)
            return CommandResult.Failed(CommandError.InvalidState, $"Vehicle {command.VehicleId} already exists.", now);

        try
        {
            var vehicle = Vehicle.Register(
                command.VehicleId, command.Plate, command.DistrictId is > 0 ? command.DistrictId : null, command.Notes, now);
            _repository.Add(vehicle);

            // SKELETON: no event is published. No other service needs vehicle changes today; a VehicleAdded event
            // would be a contract change, to agree with the team first.
            var result = CommandResult.Ok(vehicle.Id, created: true, now);
            _repository.RememberCommandResult(command.Context.IdempotencyKey, result);
            await _repository.SaveChangesAsync(ct);

            _logger.LogInformation("Vehicle {VehicleId} ({Plate}) added by {SubmittedBy}",
                vehicle.Id, vehicle.Plate, command.Context.SubmittedBy);
            return result;
        }
        catch (DomainException e)
        {
            return CommandResult.Failed(CommandError.Validation, e.Message, now);
        }
    }
}
