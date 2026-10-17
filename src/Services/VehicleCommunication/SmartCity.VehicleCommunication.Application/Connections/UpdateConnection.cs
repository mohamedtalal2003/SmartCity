using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.Contracts.Enums;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Fleet;
using SmartCity.VehicleCommunication.Domain.Frames;

namespace SmartCity.VehicleCommunication.Application.Connections;

/// <summary>
/// Use cases V6 and V18: a message on vehicles/{id}/status. "online" when the vehicle connects; "offline" from its
/// Last Will, sent by EMQX when the vehicle disappears.
/// </summary>
public sealed class UpdateConnection
{
    private readonly IVehicleRepository _repository;
    private readonly IVehicleMessenger _messenger;
    private readonly ILiveMap _liveMap;
    private readonly CaptureOptions _capture;
    private readonly FrameCheckOptions _checks;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateConnection> _logger;

    public UpdateConnection(
        IVehicleRepository repository,
        IVehicleMessenger messenger,
        ILiveMap liveMap,
        IOptions<CaptureOptions> capture,
        IOptions<FrameCheckOptions> checks,
        TimeProvider time,
        ILogger<UpdateConnection> logger)
    {
        _repository = repository;
        _messenger = messenger;
        _liveMap = liveMap;
        _capture = capture.Value;
        _checks = checks.Value;
        _time = time;
        _logger = logger;
    }

    public async Task HandleAsync(string vehicleId, bool online, CancellationToken ct)
    {
        var vehicle = await _repository.GetAsync(vehicleId, ct);
        if (vehicle is null)
        {
            _logger.LogWarning("Status from unknown vehicle {VehicleId} ignored", vehicleId);
            return;
        }

        var now = _time.GetUtcNow();
        if (online)
        {
            vehicle.MarkOnline(now);
            await _repository.TrySaveAsync(ct);
            _logger.LogInformation("Vehicle {VehicleId} is online", vehicleId);

            // V19: the config is retained already, but send it again so a just-added vehicle gets it too.
            if (vehicle.Status == VehicleStatus.Active)
                await _messenger.PublishConfigAsync(vehicleId, VehicleConfigMessage.From(_capture, _checks), ct);
        }
        else
        {
            vehicle.MarkOffline(now);
            await _repository.TrySaveAsync(ct);
            await _liveMap.RemoveAsync(vehicleId, ct);
            _logger.LogInformation("Vehicle {VehicleId} is offline (Last Will)", vehicleId);
        }
    }
}
