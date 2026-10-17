using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Fleet;

namespace SmartCity.VehicleCommunication.Application.Connections;

/// <summary>
/// Use case V18 without a Last Will (something odd happened): an Online vehicle with no heartbeat for 60 seconds is
/// marked Offline and taken off the live map. Run by a job every 30 seconds.
/// </summary>
public sealed class MarkSilentVehiclesOffline
{
    private readonly IVehicleRepository _repository;
    private readonly ILiveMap _liveMap;
    private readonly FleetOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<MarkSilentVehiclesOffline> _logger;

    public MarkSilentVehiclesOffline(
        IVehicleRepository repository,
        ILiveMap liveMap,
        IOptions<FleetOptions> options,
        TimeProvider time,
        ILogger<MarkSilentVehiclesOffline> logger)
    {
        _repository = repository;
        _liveMap = liveMap;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var silent = await _repository.ListOnlineNotSeenSinceAsync(now.AddSeconds(-_options.OfflineAfterSeconds), ct);
        if (silent.Count == 0)
            return;

        foreach (var vehicle in silent)
            vehicle.MarkOffline(now);

        if (!await _repository.TrySaveAsync(ct))
            return; // one of them changed at the same moment; the next run tries again

        foreach (var vehicle in silent)
        {
            await _liveMap.RemoveAsync(vehicle.Id, ct);
            _logger.LogInformation("Vehicle {VehicleId} marked offline: no heartbeat since {LastSeenAt}", vehicle.Id, vehicle.LastSeenAt);
        }
    }
}
