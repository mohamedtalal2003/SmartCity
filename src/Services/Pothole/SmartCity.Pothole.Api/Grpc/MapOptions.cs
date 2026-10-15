namespace SmartCity.Pothole.Api.Grpc;

/// <summary>The map numbers (P13), from the <c>Map</c> section of appsettings.</summary>
public sealed class MapOptions
{
    public const string SectionName = "Map";

    /// <summary>Fewer potholes than this in the box: one point each. More: grid clusters.</summary>
    public int MaxPoints { get; set; } = 500;

    // SKELETON: the cluster grid size is a first guess (4 × 4 cells per 256-pixel map tile); the design
    // marks clustering as a proposal.
    public int CellsPerTile { get; set; } = 4;
}
