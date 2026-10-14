using NetTopologySuite.Geometries;

namespace SmartCity.Pothole.Infrastructure.Persistence;

/// <summary>
/// One Istanbul district (ilçe) or neighborhood (mahalle) from OpenStreetMap. A plain lookup row,
/// not a domain entity: it is only used to find the area of a new pothole.
/// </summary>
public sealed class AdminArea
{
    public int Id { get; set; }
    public string OsmId { get; set; } = "";          // e.g. "relation/963209"
    public string Name { get; set; } = "";
    public AdminLevel Level { get; set; }
    public int? ParentId { get; set; }               // the district of a neighborhood; null for districts
    public MultiPolygon Geometry { get; set; } = default!;
}

public enum AdminLevel
{
    District,       // OSM admin_level 6
    Neighborhood    // OSM admin_level 8
}
