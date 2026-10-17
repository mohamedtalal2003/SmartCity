using Microsoft.Extensions.Options;

namespace SmartCity.VehicleCommunication.Application.Frames;

/// <summary>
/// At most N frames per second per vehicle (V12), so a vehicle that comes back with 500 frames cannot flood
/// the server. A simple counter per vehicle that starts again every second.
/// SKELETON: kept in memory, so it only works with one instance of the service. The real version keeps the
/// counters in Redis.
/// </summary>
public sealed class FrameRateLimiter
{
    private readonly Dictionary<string, (long Second, int Count)> _counters = new();
    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private readonly int _maxPerSecond;

    public FrameRateLimiter(TimeProvider time, IOptions<IngestOptions> options)
    {
        _time = time;
        _maxPerSecond = options.Value.MaxFramesPerSecondPerVehicle;
    }

    /// <summary>True when the vehicle may send one more frame in this second.</summary>
    public bool TryAcquire(string vehicleId)
    {
        var second = _time.GetUtcNow().ToUnixTimeSeconds();

        lock (_lock)
        {
            // Frames already sent by this vehicle in this second (0 when the second is new).
            var count = _counters.TryGetValue(vehicleId, out var counter) && counter.Second == second ? counter.Count : 0;
            if (count >= _maxPerSecond)
                return false;

            _counters[vehicleId] = (second, count + 1);
            return true;
        }
    }
}
