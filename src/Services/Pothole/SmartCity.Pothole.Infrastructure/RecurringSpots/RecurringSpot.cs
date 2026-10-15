using NetTopologySuite.Geometries;

namespace SmartCity.Pothole.Infrastructure.RecurringSpots;

/// <summary>
/// A place where potholes keep coming back (P17): two or more potholes within a few meters of each other.
/// For statistics only; <see cref="RecurringSpotsJob"/> rebuilds the whole table each run. Not part of
/// the pothole aggregate.
/// </summary>
public class RecurringSpot
{
    public Guid Id { get; set; }
    public Point Location { get; set; } = default!;      // the middle of the group
    public int? DistrictId { get; set; }                  // the district of the newest pothole in the group
    public List<Guid> PotholeIds { get; set; } = new();   // oldest first
    public int PotholeCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}
