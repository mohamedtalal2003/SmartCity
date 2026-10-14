using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Domain.Estimation;

/// <summary>
/// Pothole's own repair estimator. Pure math: no I/O, no logging. The prices are passed in.
/// The rules are the same ones the CostCalculation service uses today.
/// </summary>
public static class PotholeEstimator
{
    public static CostEstimate Estimate(
        SeverityLevel severity, DamageStage stage, double? areaM2, double? depthCm, PotholePrices prices)
    {
        // SKELETON: stage, areaM2 and depthCm are not used yet. The real version prices by area and
        // depth when the AI sends them; until then the price depends on the severity only.
        var total = severity switch
        {
            SeverityLevel.Low => prices.Low,
            SeverityLevel.Medium => prices.Medium,
            SeverityLevel.High => prices.High,
            SeverityLevel.Critical => prices.Critical,
            _ => prices.Medium
        };

        return new CostEstimate
        {
            Materials = total * prices.MaterialsShare,
            Labor = total * prices.LaborShare,
            Equipment = total * prices.EquipmentShare,
            Total = total,
            Currency = prices.Currency,
            LaborHours = severity switch
            {
                SeverityLevel.Low => 1,
                SeverityLevel.Medium => 2,
                SeverityLevel.High => 3,
                SeverityLevel.Critical => 5,
                _ => 2
            },
            CrewSize = severity >= SeverityLevel.High ? 3 : 2,
            SuggestedPriority = severity switch
            {
                SeverityLevel.Critical => PriorityLevel.Critical,
                SeverityLevel.High => PriorityLevel.High,
                SeverityLevel.Medium => PriorityLevel.Medium,
                _ => PriorityLevel.Routine
            },
            Material = new MaterialLine(
                Name: "Soğuk asfalt",
                Quantity: severity switch
                {
                    SeverityLevel.Low => 0.5,
                    SeverityLevel.Medium => 1.0,
                    SeverityLevel.High => 1.5,
                    _ => 2.0
                },
                Unit: "m³")
        };
    }
}
