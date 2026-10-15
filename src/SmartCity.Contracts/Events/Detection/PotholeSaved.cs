using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Detection;

/// <summary>
/// Published by the Pothole service when a pothole is created, or when its severity, stage or damage
/// type changes. IsNewPothole = false means a re-sighting of an existing pothole
/// (consumers like Work Order must NOT create a second ticket in that case).
///
/// Subscribers: Cost Calculation, Work Order, Notification
/// </summary>
public record PotholeSaved : IntegrationEvent
{
    public Guid PotholeId { get; init; }
    public Guid SourceDetectionId { get; init; }
    public bool IsNewPothole { get; init; } = true;
    public PotholeSavedReason Reason { get; init; }       // why it was sent: New, Changed or Corrected
    public int DetectionCount { get; init; } = 1;
    public long RowVersion { get; init; }                 // the pothole's version; a newer message has a bigger number

    public DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public GeoLocation Location { get; init; } = default!;
    public int? DistrictId { get; init; }                 // Istanbul district (ilçe) id; null = unknown
    public string? DistrictName { get; init; }            // e.g. "Kadıköy"
    public int? NeighborhoodId { get; init; }             // Istanbul neighborhood (mahalle) id; null = unknown
    public string? NeighborhoodName { get; init; }        // e.g. "Caferağa Mahallesi"
    public string? RoadName { get; init; }

    public DetectionType DetectionType { get; init; }     // Pothole / RoadCrack / SurfaceDamage
    public DamageStage Stage { get; init; }
    public SeverityLevel Severity { get; init; }
    public Dimensions? EstimatedSize { get; init; }       // area and depth, when measured
    public double Confidence { get; init; }
    public PotholeSource Source { get; init; }            // who reported it first: Ai, Officer, CitizenReport
    public CostEstimateInfo? Estimate { get; init; }      // Pothole's own repair estimate

    public List<string> ImageUrls { get; init; } = new();
}
