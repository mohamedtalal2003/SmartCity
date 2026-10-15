using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// A pothole reported by a person (an officer, or an officer entering a citizen's report), with a photo.
/// The severity is always set: when the person gave only a size, the use case works it out first.
/// </summary>
public sealed record OfficerReport(
    GeoPoint Location,
    string ImageUrl,
    SeverityLevel Severity,
    double? AreaM2,
    double? DepthCm,
    PotholeSource Source,
    string? Notes,
    string CreatedBy,
    DateTimeOffset At);
