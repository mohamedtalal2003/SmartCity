namespace SmartCity.WorkOrder.Api.Domain;

public class PendingCostEstimate
{
    public Guid Id { get; set; }
    public Guid SourceEntityId { get; set; }
    public decimal TotalEstimatedCost { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
