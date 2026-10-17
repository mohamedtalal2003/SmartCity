using Microsoft.Extensions.Logging;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Commands;
using SmartCity.VehicleCommunication.Domain.Vehicles;

namespace SmartCity.VehicleCommunication.Application.Vehicles;

/// <summary>
/// The admin changes a vehicle (V2). A value left null stays as it is. For the district, 0 clears it.
/// </summary>
public sealed record UpdateVehicleCommand(CommandContext Context, string VehicleId, string? Plate, int? DistrictId, string? Notes);

/// <summary>Use case V2: the admin changes the plate, district or notes. The vehicle id never changes.</summary>
public sealed class UpdateVehicle
{
    private readonly IVehicleRepository _repository;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateVehicle> _logger;

    public UpdateVehicle(IVehicleRepository repository, TimeProvider time, ILogger<UpdateVehicle> logger)
    {
        _repository = repository;
        _time = time;
        _logger = logger;
    }

    public async Task<CommandResult> HandleAsync(UpdateVehicleCommand command, CancellationToken ct)
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
            var districtId = command.DistrictId switch
            {
                null => vehicle.DistrictId,     // not given: unchanged
                0 => null,                      // 0: clear it
                var id => id
            };
            vehicle.Update(command.Plate ?? vehicle.Plate, districtId, command.Notes ?? vehicle.Notes, now);

            // SKELETON: no event is published (see AddVehicle).
            var result = CommandResult.Ok(vehicle.Id, created: false, now);
            _repository.RememberCommandResult(command.Context.IdempotencyKey, result);
            await _repository.SaveChangesAsync(ct);

            _logger.LogInformation("Vehicle {VehicleId} updated by {SubmittedBy}", vehicle.Id, command.Context.SubmittedBy);
            return result;
        }
        catch (DomainException e)
        {
            return CommandResult.Failed(CommandError.Validation, e.Message, now);
        }
    }
}
