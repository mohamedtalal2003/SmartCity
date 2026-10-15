using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.Contracts.Events.Detection;
using SmartCity.Pothole.Application.Abstractions;
using SmartCity.Pothole.Domain.Estimation;

namespace SmartCity.Pothole.Application.Potholes;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Builds <see cref="PotholeSaved"/> from the pothole's current state. Every use case that publishes it
/// uses this, so the event looks the same whether the AI, an officer or a correction changed the pothole.
/// </summary>
public static class PotholeSavedMessage
{
    public static PotholeSaved Build(
        Pothole pothole,
        AreaMatch area,
        Guid correlationId,
        Guid sourceDetectionId,
        PotholeSavedReason reason,
        List<string> imageUrls,
        PotholePrices prices)
    {
        // Only the money is stored on the pothole, so compute the estimate fresh from the current values.
        // It is pure math, and this way the hours, crew, priority and material lines are always complete.
        var estimate = PotholeEstimator.Estimate(
            pothole.CurrentSeverity, pothole.CurrentStage, pothole.AreaM2, pothole.DepthCm, prices);

        return new PotholeSaved
        {
            // Invariant 5: the caller passes the CorrelationId it received, so the trace works.
            CorrelationId = correlationId,
            SourceDetectionId = sourceDetectionId,
            PotholeId = pothole.Id,
            IsNewPothole = reason == PotholeSavedReason.New,
            Reason = reason,
            DetectionCount = pothole.SightingsCount,
            RowVersion = pothole.RowVersion,
            FirstSeenAt = pothole.FirstSeenAt,
            LastSeenAt = pothole.LastSeenAt,
            Location = new GeoLocation { Latitude = pothole.Location.Latitude, Longitude = pothole.Location.Longitude },
            DistrictId = area.DistrictId,
            DistrictName = area.DistrictName,
            NeighborhoodId = area.NeighborhoodId,
            NeighborhoodName = area.NeighborhoodName,
            RoadName = pothole.RoadName,
            DetectionType = pothole.DamageType,
            Stage = pothole.CurrentStage,
            Severity = pothole.CurrentSeverity,
            // The pothole keeps the depth in cm; Dimensions has it in meters.
            EstimatedSize = pothole.AreaM2 is null && pothole.DepthCm is null
                ? null
                : new Dimensions { AreaSquareMeters = pothole.AreaM2, DepthMeters = pothole.DepthCm / 100 },
            Confidence = pothole.Confidence,
            Source = ToContractSource(pothole.Source),
            Estimate = ToEstimateInfo(estimate),
            ImageUrls = imageUrls
        };
    }

    // Pothole's own enum and the contract enum have the same names, so map them one by one.
    public static Contracts.Enums.PotholeSource ToContractSource(PotholeSource source) => source switch
    {
        PotholeSource.Ai => Contracts.Enums.PotholeSource.Ai,
        PotholeSource.Officer => Contracts.Enums.PotholeSource.Officer,
        PotholeSource.CitizenReport => Contracts.Enums.PotholeSource.CitizenReport,
        _ => Contracts.Enums.PotholeSource.Unspecified
    };

    private static CostEstimateInfo ToEstimateInfo(CostEstimate estimate) => new()
    {
        Materials = estimate.Materials,
        Labor = estimate.Labor,
        Equipment = estimate.Equipment,
        Total = estimate.Total,
        Currency = estimate.Currency,
        LaborHours = estimate.LaborHours,
        CrewSize = estimate.CrewSize,
        SuggestedPriority = estimate.SuggestedPriority,
        MaterialItems = new List<MaterialItem>
        {
            new()
            {
                Name = estimate.Material.Name,
                Quantity = estimate.Material.Quantity,
                Unit = estimate.Material.Unit,
                UnitCost = estimate.Material.Quantity > 0 ? estimate.Materials / (decimal)estimate.Material.Quantity : 0,
                TotalCost = estimate.Materials
            }
        }
    };
}
