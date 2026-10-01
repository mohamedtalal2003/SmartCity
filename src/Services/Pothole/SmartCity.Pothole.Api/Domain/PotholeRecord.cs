using NetTopologySuite.Geometries;
using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Api.Domain;

public class PotholeRecord
{
    public Guid Id { get; set; }
    public Guid SourceDetectionId { get; set; }
    public Guid CorrelationId { get; set; }
    public Point Location { get; set; } = default!;
    public District District { get; set; }
    public string? RoadName { get; set; }
    public DetectionType DetectionType { get; set; }
    public DamageStage Stage { get; set; }
    public SeverityLevel Severity { get; set; }
    public double Confidence { get; set; }
    public int DetectionCount { get; set; } = 1;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public PotholeStatus Status { get; set; } = PotholeStatus.Open;
    public string[] ImageUrls { get; set; } = Array.Empty<string>();
}

public enum PotholeStatus
{
    Open,
    Fixed
}
