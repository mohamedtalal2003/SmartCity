using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Application.Detections;

/// <summary>The matching numbers, from the <c>Detection</c> section of appsettings.</summary>
public sealed class DetectionOptions
{
    public const string SectionName = "Detection";

    /// <summary>A detection this close to an Open pothole is the same pothole.</summary>
    public double MatchRadiusMeters { get; set; } = 3;

    /// <summary>Below this AI confidence a detection does not create a new pothole.</summary>
    public double MinConfidenceForNew { get; set; } = 0.5;

    /// <summary>For this many days, a place closed as FalsePositive does not get a new pothole from the AI.</summary>
    public int FalsePositiveSuppressionDays { get; set; } = 30;

    // Severity from the estimated depth in cm: below LowBelowCm is Low, below MediumBelowCm is Medium,
    // below HighBelowCm is High, anything deeper is Critical.
    public double LowBelowCm { get; set; } = 4;
    public double MediumBelowCm { get; set; } = 7;
    public double HighBelowCm { get; set; } = 10;

    /// <summary>The severity for a depth, with the thresholds above. The AI path and officer adds both use it.</summary>
    public SeverityLevel SeverityFromDepth(double depthCm)
    {
        if (depthCm < LowBelowCm) return SeverityLevel.Low;
        if (depthCm < MediumBelowCm) return SeverityLevel.Medium;
        if (depthCm < HighBelowCm) return SeverityLevel.High;
        return SeverityLevel.Critical;
    }
}
