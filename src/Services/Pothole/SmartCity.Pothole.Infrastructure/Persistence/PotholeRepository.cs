using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SmartCity.Pothole.Application.Abstractions;

namespace SmartCity.Pothole.Infrastructure.Persistence;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>EF Core + PostGIS implementation of <see cref="IPotholeRepository"/>.</summary>
public sealed class PotholeRepository : IPotholeRepository
{
    private readonly PotholeDbContext _db;

    public PotholeRepository(PotholeDbContext db) => _db = db;

    public Task<bool> ObservationExistsAsync(Guid detectionId, CancellationToken ct) =>
        _db.Observations.AnyAsync(o => o.SourceDetectionId == detectionId, ct);

    public async Task<Pothole?> FindNearestOpenAsync(GeoPoint point, double radiusM, CancellationToken ct)
    {
        // PostGIS on geography measures in meters. Uses the partial GIST index on open potholes.
        var nearest = await _db.Potholes
            .FromSqlInterpolated($"""
                SELECT * FROM potholes
                WHERE status = 'Open'
                  AND ST_DWithin(location, ST_SetSRID(ST_MakePoint({point.Longitude}, {point.Latitude}), 4326)::geography, {radiusM})
                ORDER BY ST_Distance(location, ST_SetSRID(ST_MakePoint({point.Longitude}, {point.Latitude}), 4326)::geography)
                LIMIT 1
                """)
            .ToListAsync(ct);

        return nearest.FirstOrDefault();
    }

    public async Task<Pothole?> FindRecentFalsePositiveAsync(GeoPoint point, double radiusM, DateTimeOffset since, CancellationToken ct)
    {
        // Uses the partial GIST index on potholes closed as FalsePositive.
        var nearest = await _db.Potholes
            .FromSqlInterpolated($"""
                SELECT * FROM potholes
                WHERE closed_reason = 'FalsePositive'
                  AND closed_at >= {since}
                  AND ST_DWithin(location, ST_SetSRID(ST_MakePoint({point.Longitude}, {point.Latitude}), 4326)::geography, {radiusM})
                ORDER BY ST_Distance(location, ST_SetSRID(ST_MakePoint({point.Longitude}, {point.Latitude}), 4326)::geography)
                LIMIT 1
                """)
            .ToListAsync(ct);

        return nearest.FirstOrDefault();
    }

    public async Task<AreaMatch> FindAreaAsync(GeoPoint point, CancellationToken ct)
    {
        var location = new Point(point.Longitude, point.Latitude) { SRID = 4326 };

        var district = await _db.AdminAreas
            .Where(a => a.Level == AdminLevel.District && a.Geometry.Intersects(location))
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Name })
            .FirstOrDefaultAsync(ct);

        var neighborhood = await _db.AdminAreas
            .Where(a => a.Level == AdminLevel.Neighborhood && a.Geometry.Intersects(location))
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Name })
            .FirstOrDefaultAsync(ct);

        return new AreaMatch(district?.Id, district?.Name, neighborhood?.Id, neighborhood?.Name);
    }

    public async Task<AreaMatch> GetAreaAsync(int? districtId, int? neighborhoodId, CancellationToken ct)
    {
        var districtName = districtId is null
            ? null
            : await _db.AdminAreas.Where(a => a.Id == districtId).Select(a => a.Name).FirstOrDefaultAsync(ct);

        var neighborhoodName = neighborhoodId is null
            ? null
            : await _db.AdminAreas.Where(a => a.Id == neighborhoodId).Select(a => a.Name).FirstOrDefaultAsync(ct);

        return new AreaMatch(districtId, districtName, neighborhoodId, neighborhoodName);
    }

    public Task<Pothole?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Potholes.FirstOrDefaultAsync(p => p.Id == id, ct);

    public void Add(Pothole pothole) => _db.Potholes.Add(pothole);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
