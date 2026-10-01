using SmartCity.Contracts.Enums;

namespace SmartCity.CostCalculation.Api.Domain;

public class CostEstimate
{
    public Guid Id { get; set; }
    public Guid SourceDetectionId { get; set; }
    public Guid SourceEntityId { get; set; }
    public SourceEntityType SourceEntityType { get; set; }
    public Guid CorrelationId { get; set; }
    public SeverityLevel Severity { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal EquipmentCost { get; set; }
    public decimal TotalEstimatedCost { get; set; }
    public string Currency { get; set; } = "TRY";
    public double EstimatedLaborHours { get; set; }
    public int EstimatedCrewSize { get; set; }
    public PriorityLevel SuggestedPriority { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
}
