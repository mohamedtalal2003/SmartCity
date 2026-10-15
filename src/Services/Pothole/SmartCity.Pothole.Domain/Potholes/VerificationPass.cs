using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>A vehicle drove past a repaired pothole and the AI checked the photo (P10).</summary>
public sealed record VerificationPass(
    Guid FrameId,
    VerificationResult Result,
    double Confidence,
    string ImageUrl,
    DateTimeOffset ObservedAt);
