using Microsoft.Extensions.Logging;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.VehicleCommunication.Application.Abstractions;

namespace SmartCity.VehicleCommunication.Application.Connections;

/// <summary>One heartbeat from vehicles/{id}/telemetry (the fields this service uses).</summary>
public sealed record TelemetryMessage(
    string VehicleId,
    DateTimeOffset CapturedAt,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    double? Heading,
    double SpeedKmh,
    int PendingFrames,
    string? RouteId,
    double BatteryPercent,
    bool CameraOnline,
    string NetworkType,
    int SignalStrengthDbm);

/// <summary>
/// Use case V7: a heartbeat. Updates the live map, publishes <see cref="VehicleTelemetryReceived"/> (as before), and
/// writes last seen, location and pending frames to the vehicle's row at most every 30 seconds.
/// A heartbeat from an unknown or not Active vehicle is logged and ignored.
/// </summary>
public sealed class RecordTelemetry
{
    private readonly IVehicleRepository _repository;
    private readonly ILiveMap _liveMap;
    private readonly ITelemetryPublisher _publisher;
    private readonly HeartbeatThrottle _throttle;
    private readonly TimeProvider _time;
    private readonly ILogger<RecordTelemetry> _logger;

    public RecordTelemetry(
        IVehicleRepository repository,
        ILiveMap liveMap,
        ITelemetryPublisher publisher,
        HeartbeatThrottle throttle,
        TimeProvider time,
        ILogger<RecordTelemetry> logger)
    {
        _repository = repository;
        _liveMap = liveMap;
        _publisher = publisher;
        _throttle = throttle;
        _time = time;
        _logger = logger;
    }

    public async Task HandleAsync(TelemetryMessage message, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        // SKELETON: one read per heartbeat to know the vehicle and its status. With many vehicles, keep a short
        // in-memory cache of the statuses instead.
        var vehicle = await _repository.GetAsync(message.VehicleId, ct);
        if (vehicle is null)
        {
            _logger.LogWarning("Telemetry from unknown vehicle {VehicleId} ignored", message.VehicleId);
            return;
        }
        if (vehicle.Status != VehicleStatus.Active)
        {
            _logger.LogDebug("Telemetry from vehicle {VehicleId} ignored: it is {Status}", message.VehicleId, vehicle.Status);
            return;
        }

        await _liveMap.UpdateAsync(new LivePosition(
            message.VehicleId, message.Latitude, message.Longitude, message.SpeedKmh, message.Heading, message.CapturedAt), ct);

        await _publisher.PublishAsync(new VehicleTelemetryReceived
        {
            CorrelationId = Guid.NewGuid(),
            VehicleId = message.VehicleId,
            VehiclePlate = vehicle.Plate,
            CapturedAt = message.CapturedAt,
            Location = new GeoLocation
            {
                Latitude = message.Latitude,
                Longitude = message.Longitude,
                AccuracyMeters = message.AccuracyMeters,
                Heading = message.Heading
            },
            SpeedKmh = message.SpeedKmh,
            RouteId = message.RouteId,
            BatteryPercent = message.BatteryPercent,
            CameraOnline = message.CameraOnline,
            NetworkType = message.NetworkType,
            SignalStrengthDbm = message.SignalStrengthDbm
        }, ct);

        // The row: at most every 30 s, but at once when the vehicle was Offline (it is back).
        if (!_throttle.IsDue(message.VehicleId, now) && vehicle.ConnectionStatus == VehicleConnectionStatus.Online)
            return;

        // Last seen is the server's time, so a vehicle with a wrong clock is not marked Offline by mistake.
        vehicle.RecordHeartbeat(now, message.Latitude, message.Longitude, message.PendingFrames);
        if (await _repository.TrySaveAsync(ct))
            _throttle.Written(message.VehicleId, now);
        // else: an admin changed the vehicle at the same moment; the next heartbeat writes it.
    }
}
