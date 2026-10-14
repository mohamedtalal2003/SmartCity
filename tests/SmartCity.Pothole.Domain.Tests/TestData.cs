using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Domain.Estimation;

namespace SmartCity.Pothole.Domain.Tests;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>Builders shared by the tests.</summary>
internal static class TestData
{
    public static readonly GeoPoint Kadikoy = new(40.985, 29.030);

    // The numbers the CostCalculation service uses today (also in the Pothole appsettings.json).
    public static readonly PotholePrices Prices = new()
    {
        Low = 1000m,
        Medium = 1800m,
        High = 2500m,
        Critical = 4000m,
        MaterialsShare = 0.36m,
        LaborShare = 0.48m,
        EquipmentShare = 0.16m,
        Currency = "TRY"
    };

    public static Detection Detection(
        DetectionType type = DetectionType.Pothole,
        SeverityLevel severity = SeverityLevel.Medium,
        DateTimeOffset? seenAt = null) =>
        new(
            DetectionId: Guid.NewGuid(),
            Location: Kadikoy,
            DamageType: type,
            Severity: severity,
            Stage: type == DetectionType.Pothole ? DamageStage.Pothole : DamageStage.Unspecified,
            AreaM2: null,
            DepthCm: 5,
            Confidence: 0.9,
            ImageUrl: "http://localhost:9000/frames-temp/MUN-VEH-001/frame.jpg",
            SeenAt: seenAt ?? new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));

    public static Pothole NewPothole(
        DetectionType type = DetectionType.Pothole,
        SeverityLevel severity = SeverityLevel.Medium) =>
        Pothole.FromDetection(Detection(type, severity), districtId: 1, neighborhoodId: 2, Prices);
}
