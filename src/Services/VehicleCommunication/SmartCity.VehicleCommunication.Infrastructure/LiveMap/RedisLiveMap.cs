using System.Globalization;
using SmartCity.VehicleCommunication.Application.Abstractions;
using StackExchange.Redis;

namespace SmartCity.VehicleCommunication.Infrastructure.LiveMap;

/// <summary>
/// The live map in Redis: one hash per vehicle, <c>vehicle:pos:{id}</c> with lat, lon, speed, heading and at, that
/// expires after 60 s without a heartbeat. Same keys as before; the Gateway reads them for GET /api/vehicles/live.
/// </summary>
public sealed class RedisLiveMap : ILiveMap
{
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(60);

    private readonly IConnectionMultiplexer _redis;

    public RedisLiveMap(IConnectionMultiplexer redis) => _redis = redis;

    public async Task UpdateAsync(LivePosition position, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = Key(position.VehicleId);
        await db.HashSetAsync(key, new HashEntry[]
        {
            new("lat", position.Latitude.ToString("F6", CultureInfo.InvariantCulture)),
            new("lon", position.Longitude.ToString("F6", CultureInfo.InvariantCulture)),
            new("speed", position.SpeedKmh.ToString("F1", CultureInfo.InvariantCulture)),
            new("heading", (position.Heading ?? 0).ToString("F1", CultureInfo.InvariantCulture)),
            new("at", position.At.ToString("O")),
        });
        await db.KeyExpireAsync(key, Expiry);
    }

    public Task RemoveAsync(string vehicleId, CancellationToken ct) =>
        _redis.GetDatabase().KeyDeleteAsync(Key(vehicleId));

    private static string Key(string vehicleId) => $"vehicle:pos:{vehicleId}";
}
