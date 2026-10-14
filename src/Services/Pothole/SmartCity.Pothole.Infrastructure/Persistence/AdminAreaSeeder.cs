using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace SmartCity.Pothole.Infrastructure.Persistence;

/// <summary>
/// Loads the Istanbul districts and neighborhoods (OpenStreetMap GeoJSON in Persistence/SeedData) into
/// admin_areas once, at startup after the migrations. If the table already has rows, it does nothing.
/// </summary>
public sealed class AdminAreaSeeder
{
    private readonly PotholeDbContext _db;
    private readonly ILogger<AdminAreaSeeder> _logger;

    public AdminAreaSeeder(PotholeDbContext db, ILogger<AdminAreaSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        // SKELETON: two instances starting at the same moment could both seed. The real version takes a
        // database lock (or seeds in a migration) first.
        if (await _db.AdminAreas.AnyAsync(ct))
            return;

        var districts = ReadAreas("istanbul-districts.geojson", AdminLevel.District);
        var neighborhoods = ReadAreas("istanbul-neighborhoods.geojson", AdminLevel.Neighborhood);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.AdminAreas.AddRange(districts);
        _db.AdminAreas.AddRange(neighborhoods);
        await _db.SaveChangesAsync(ct);

        // A neighborhood's parent is the district that contains a point inside the neighborhood.
        await _db.Database.ExecuteSqlRawAsync("""
            UPDATE admin_areas AS n
            SET parent_id = d.id
            FROM admin_areas AS d
            WHERE n.level = 'Neighborhood'
              AND d.level = 'District'
              AND ST_Contains(d.geometry, ST_PointOnSurface(n.geometry))
            """, ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation("Loaded {Districts} Istanbul districts and {Neighborhoods} neighborhoods",
            districts.Count, neighborhoods.Count);
    }

    private static List<AdminArea> ReadAreas(string fileName, AdminLevel level)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Persistence", "SeedData", fileName);

        var json = new JsonSerializerOptions();
        json.Converters.Add(new GeoJsonConverterFactory());

        using var stream = File.OpenRead(path);
        var features = JsonSerializer.Deserialize<FeatureCollection>(stream, json)
            ?? throw new InvalidOperationException($"{fileName} has no features.");

        return features
            .Select(feature => new AdminArea
            {
                OsmId = Convert.ToString(feature.Attributes["osm_id"], CultureInfo.InvariantCulture)!,
                Name = Convert.ToString(feature.Attributes["name"], CultureInfo.InvariantCulture)!,
                Level = level,
                Geometry = ToMultiPolygon(feature.Geometry)
            })
            .ToList();
    }

    // The column holds MultiPolygons only, so a single Polygon is wrapped in one.
    private static MultiPolygon ToMultiPolygon(Geometry geometry)
    {
        var multiPolygon = geometry switch
        {
            MultiPolygon m => m,
            Polygon p => new MultiPolygon(new[] { p }),
            _ => throw new InvalidOperationException($"Unexpected geometry type {geometry.GeometryType}.")
        };

        multiPolygon.SRID = 4326;
        return multiPolygon;
    }
}
