using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>One sighting of road damage coming in from the AI.</summary>
public sealed record Detection(
    Guid DetectionId,
    GeoPoint Location,
    DetectionType DamageType,
    SeverityLevel Severity,
    DamageStage Stage,
    double? AreaM2,
    double? DepthCm,
    double Confidence,
    string ImageUrl,
    DateTimeOffset SeenAt);
