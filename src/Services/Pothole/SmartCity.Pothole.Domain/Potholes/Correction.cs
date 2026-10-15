using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>An officer fixes the pothole's data. A value left null stays as it is. The reason is required.</summary>
public sealed record Correction(
    SeverityLevel? Severity,
    DamageStage? Stage,
    double? AreaM2,
    double? DepthCm,
    string Reason,
    string CreatedBy,
    DateTimeOffset At);
