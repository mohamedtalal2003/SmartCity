namespace SmartCity.Pothole.Application.Abstractions;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// The only data port of the Pothole use cases. One named method for each thing a use case needs.
/// Implemented in Infrastructure with EF Core and PostGIS.
/// </summary>
public interface IPotholeRepository
{
    /// <summary>True when an observation for this AI detection was already saved.</summary>
    Task<bool> ObservationExistsAsync(Guid detectionId, CancellationToken ct);

    /// <summary>The nearest Open pothole within <paramref name="radiusM"/> meters, or null.</summary>
    Task<Pothole?> FindNearestOpenAsync(GeoPoint point, double radiusM, CancellationToken ct);

    /// <summary>The nearest pothole closed as FalsePositive since <paramref name="since"/> within the radius, or null.</summary>
    Task<Pothole?> FindRecentFalsePositiveAsync(GeoPoint point, double radiusM, DateTimeOffset since, CancellationToken ct);

    /// <summary>The district and neighborhood that contain the point.</summary>
    Task<AreaMatch> FindAreaAsync(GeoPoint point, CancellationToken ct);

    /// <summary>The names of a pothole's district and neighborhood, for the events.</summary>
    Task<AreaMatch> GetAreaAsync(int? districtId, int? neighborhoodId, CancellationToken ct);

    Task<Pothole?> GetByIdAsync(Guid id, CancellationToken ct);

    void Add(Pothole pothole);

    /// <summary>Saves everything in one transaction, together with the events in the outbox.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
