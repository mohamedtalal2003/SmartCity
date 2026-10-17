using Microsoft.Extensions.Options;
using SmartCity.VehicleCommunication.Application.Fleet;

namespace SmartCity.VehicleCommunication.Application.Connections;

/// <summary>
/// Heartbeats come every 2 seconds, but a vehicle's row is written at most every 30 seconds (V7), so the
/// database is not hit for every heartbeat. Remembers, per vehicle, when its row was last written.
/// SKELETON: in memory, so it only works with one instance of the service.
/// </summary>
public sealed class HeartbeatThrottle
{
    private readonly Dictionary<string, DateTimeOffset> _lastWrite = new();
    private readonly object _lock = new();
    private readonly TimeSpan _interval;

    public HeartbeatThrottle(IOptions<FleetOptions> options) =>
        _interval = TimeSpan.FromSeconds(options.Value.HeartbeatWriteSeconds);

    /// <summary>True when the vehicle's row was not written in the last interval.</summary>
    public bool IsDue(string vehicleId, DateTimeOffset now)
    {
        lock (_lock)
            return !_lastWrite.TryGetValue(vehicleId, out var last) || now - last >= _interval;
    }

    public void Written(string vehicleId, DateTimeOffset at)
    {
        lock (_lock)
            _lastWrite[vehicleId] = at;
    }
}
