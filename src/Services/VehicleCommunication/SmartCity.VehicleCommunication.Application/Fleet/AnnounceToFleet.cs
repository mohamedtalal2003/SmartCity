using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Domain.Frames;

namespace SmartCity.VehicleCommunication.Application.Fleet;

/// <summary>
/// Runs every time the service connects to MQTT (at startup and after a reconnect): publishes the retained
/// fleet/status (running at startup) and the config of every Active vehicle (V19), so a vehicle that connects
/// gets both at once.
/// </summary>
public sealed class AnnounceToFleet
{
    private readonly IVehicleRepository _repository;
    private readonly IVehicleMessenger _messenger;
    private readonly FleetStatus _fleetStatus;
    private readonly CaptureOptions _capture;
    private readonly FrameCheckOptions _checks;
    private readonly ILogger<AnnounceToFleet> _logger;

    public AnnounceToFleet(
        IVehicleRepository repository,
        IVehicleMessenger messenger,
        FleetStatus fleetStatus,
        IOptions<CaptureOptions> capture,
        IOptions<FrameCheckOptions> checks,
        ILogger<AnnounceToFleet> logger)
    {
        _repository = repository;
        _messenger = messenger;
        _fleetStatus = fleetStatus;
        _capture = capture.Value;
        _checks = checks.Value;
        _logger = logger;
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        await _fleetStatus.PublishCurrentAsync(ct);

        var config = VehicleConfigMessage.From(_capture, _checks);
        var vehicleIds = await _repository.ListActiveVehicleIdsAsync(ct);
        foreach (var vehicleId in vehicleIds)
            await _messenger.PublishConfigAsync(vehicleId, config, ct);

        _logger.LogInformation("Published fleet/status and the config of {Count} active vehicles", vehicleIds.Count);
    }
}
