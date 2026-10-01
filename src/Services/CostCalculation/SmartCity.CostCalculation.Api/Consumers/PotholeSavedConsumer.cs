using MassTransit;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.Contracts.Events.Detection;
using SmartCity.CostCalculation.Api.Data;
using SmartCity.CostCalculation.Api.Domain;

namespace SmartCity.CostCalculation.Api.Consumers;

public sealed class PotholeSavedConsumer : IConsumer<PotholeSaved>
{
    private readonly CostCalcDbContext _db;
    private readonly ILogger<PotholeSavedConsumer> _logger;

    public PotholeSavedConsumer(CostCalcDbContext db, ILogger<PotholeSavedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PotholeSaved> context)
    {
        var msg = context.Message;

        if (!msg.IsNewPothole)
            return;

        // SKELETON: real version uses a price catalog from config/DB; this uses fixed prices by severity.
        var (total, materialCost, laborCost, equipmentCost) = GetCostBySeverity(msg.Severity);

        var estimate = new CostEstimate
        {
            Id = Guid.NewGuid(),
            SourceDetectionId = msg.SourceDetectionId,
            SourceEntityId = msg.PotholeId,
            SourceEntityType = SourceEntityType.RoadDamage,
            CorrelationId = msg.CorrelationId,
            Severity = msg.Severity,
            MaterialCost = materialCost,
            LaborCost = laborCost,
            EquipmentCost = equipmentCost,
            TotalEstimatedCost = total,
            EstimatedLaborHours = msg.Severity switch
            {
                SeverityLevel.Low => 1,
                SeverityLevel.Medium => 2,
                SeverityLevel.High => 3,
                SeverityLevel.Critical => 5,
                _ => 2
            },
            EstimatedCrewSize = msg.Severity >= SeverityLevel.High ? 3 : 2,
            SuggestedPriority = msg.Severity switch
            {
                SeverityLevel.Critical => PriorityLevel.Critical,
                SeverityLevel.High => PriorityLevel.High,
                SeverityLevel.Medium => PriorityLevel.Medium,
                _ => PriorityLevel.Routine
            },
            CalculatedAt = DateTimeOffset.UtcNow
        };

        _db.CostEstimates.Add(estimate);

        var generated = new CostEstimateGenerated
        {
            CorrelationId = msg.CorrelationId,
            SourceDetectionId = msg.SourceDetectionId,
            EstimateId = estimate.Id,
            SourceEntityId = msg.PotholeId,
            SourceEntityType = SourceEntityType.RoadDamage,
            MaterialCost = materialCost,
            LaborCost = laborCost,
            EquipmentCost = equipmentCost,
            TotalEstimatedCost = total,
            EstimatedLaborHours = estimate.EstimatedLaborHours,
            EstimatedCrewSize = estimate.EstimatedCrewSize,
            SuggestedPriority = estimate.SuggestedPriority,
            Materials = new List<MaterialItem>
            {
                new()
                {
                    Name = "Soğuk asfalt",
                    Quantity = msg.Severity switch
                    {
                        SeverityLevel.Low => 0.5,
                        SeverityLevel.Medium => 1.0,
                        SeverityLevel.High => 1.5,
                        _ => 2.0
                    },
                    Unit = "m³",
                    UnitCost = materialCost,
                    TotalCost = materialCost
                }
            }
        };

        await context.PublishFollowUp(generated, context.CancellationToken);
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Generated cost estimate {EstimateId} for pothole {PotholeId}: {Total} TRY",
            estimate.Id, msg.PotholeId, total);
    }

    // SKELETON: real version reads from a price catalog; these are fixed stub prices by severity (TRY).
    private static (decimal total, decimal material, decimal labor, decimal equipment) GetCostBySeverity(SeverityLevel severity)
    {
        var total = severity switch
        {
            SeverityLevel.Low => 1000m,
            SeverityLevel.Medium => 1800m,
            SeverityLevel.High => 2500m,
            SeverityLevel.Critical => 4000m,
            _ => 1800m
        };

        return (total, total * 0.36m, total * 0.48m, total * 0.16m);
    }
}
