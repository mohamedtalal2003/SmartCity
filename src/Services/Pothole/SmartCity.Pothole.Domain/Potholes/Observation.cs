using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// One time the pothole was seen, corrected or checked. Rows are only added, never updated or deleted.
/// Each row keeps its own damage type, so the timeline shows a crack turning into a pothole.
/// </summary>
public sealed class Observation
{
    private Observation() { } // for EF

    public Guid Id { get; private set; }
    public Guid PotholeId { get; private set; }
    public Guid? SourceDetectionId { get; private set; }   // null for officer rows (later)
    public ObservationKind Kind { get; private set; }
    public DetectionType DamageType { get; private set; }
    public SeverityLevel Severity { get; private set; }
    public DamageStage Stage { get; private set; }
    public double? AreaM2 { get; private set; }
    public double? DepthCm { get; private set; }
    public double? Confidence { get; private set; }
    public string? VerificationResult { get; private set; } // only for VerificationPass (later)
    public string ImageUrl { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateTimeOffset SeenAt { get; private set; }
    public string CreatedBy { get; private set; } = default!;

    internal static Observation FromDetection(Guid potholeId, Detection d) => new()
    {
        Id = Guid.NewGuid(),
        PotholeId = potholeId,
        SourceDetectionId = d.DetectionId,
        Kind = ObservationKind.AiDetection,
        DamageType = d.DamageType,
        Severity = d.Severity,
        Stage = d.Stage,
        AreaM2 = d.AreaM2,
        DepthCm = d.DepthCm,
        Confidence = d.Confidence,
        ImageUrl = d.ImageUrl,
        SeenAt = d.SeenAt,
        CreatedBy = "ai"
    };
}
