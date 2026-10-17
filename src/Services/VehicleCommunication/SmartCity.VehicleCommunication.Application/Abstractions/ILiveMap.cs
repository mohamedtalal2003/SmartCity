namespace SmartCity.VehicleCommunication.Application.Abstractions;

/// <summary>Where a vehicle is right now, for the live map.</summary>
public sealed record LivePosition(string VehicleId, double Latitude, double Longitude, double SpeedKmh, double? Heading, DateTimeOffset At);

/// <summary>The live map of the vehicles (Redis; the Gateway reads it for GET /api/vehicles/live).</summary>
public interface ILiveMap
{
    Task UpdateAsync(LivePosition position, CancellationToken ct);

    /// <summary>Takes an offline vehicle off the map (V18).</summary>
    Task RemoveAsync(string vehicleId, CancellationToken ct);
}
