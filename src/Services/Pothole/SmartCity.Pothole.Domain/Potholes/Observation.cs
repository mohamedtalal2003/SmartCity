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
    public Guid? SourceDetectionId { get; private set; }   // null for officer rows
    public ObservationKind Kind { get; private set; }
    public DetectionType DamageType { get; private set; }
    public SeverityLevel Severity { get; private set; }
    public DamageStage Stage { get; private set; }
    public double? AreaM2 { get; private set; }
    public double? DepthCm { get; private set; }
    public double? Confidence { get; private set; }
    public string? VerificationResult { get; private set; } // only for VerificationPass: Gone, StillThere, NotVisible
    public Guid? FrameId { get; private set; }              // only for VerificationPass: the checked frame
    public string? ImageUrl { get; private set; }         // null for a correction (no photo)
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

    internal static Observation FromOfficerReport(Guid potholeId, OfficerReport r) => new()
    {
        Id = Guid.NewGuid(),
        PotholeId = potholeId,
        Kind = ObservationKind.OfficerAdd,
        DamageType = DetectionType.Pothole,
        Severity = r.Severity,
        Stage = DamageStage.Pothole,
        AreaM2 = r.AreaM2,
        DepthCm = r.DepthCm,
        ImageUrl = r.ImageUrl,
        Notes = r.Notes,
        SeenAt = r.At,
        CreatedBy = r.CreatedBy
    };

    /// <summary>A verification photo. The pothole's own values are copied so the timeline row is complete.</summary>
    internal static Observation FromVerificationPass(Pothole p, VerificationPass v) => new()
    {
        Id = Guid.NewGuid(),
        PotholeId = p.Id,
        Kind = ObservationKind.VerificationPass,
        DamageType = p.DamageType,
        Severity = p.CurrentSeverity,
        Stage = p.CurrentStage,
        AreaM2 = p.AreaM2,
        DepthCm = p.DepthCm,
        Confidence = v.Confidence,
        VerificationResult = v.Result.ToString(),
        FrameId = v.FrameId,
        ImageUrl = v.ImageUrl,
        SeenAt = v.ObservedAt,
        CreatedBy = "ai"
    };

    /// <summary>The values after the correction, with the reason in <see cref="Notes"/>.</summary>
    internal static Observation FromCorrection(Pothole p, Correction c) => new()
    {
        Id = Guid.NewGuid(),
        PotholeId = p.Id,
        Kind = ObservationKind.Correction,
        DamageType = p.DamageType,
        Severity = p.CurrentSeverity,
        Stage = p.CurrentStage,
        AreaM2 = p.AreaM2,
        DepthCm = p.DepthCm,
        Notes = c.Reason,
        SeenAt = c.At,
        CreatedBy = c.CreatedBy
    };
}
