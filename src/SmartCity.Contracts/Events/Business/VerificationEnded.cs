using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Business;

/// <summary>
/// Published by Work Order when a ticket leaves AwaitingReview, so the place leaves the list of
/// places to check (scenario P10).
///
/// Subscribers: Vehicle Communication (removes the place from the vehicles' list).
/// Not published by anyone yet.
/// </summary>
public record VerificationEnded : IntegrationEvent
{
    public Guid WorkOrderId { get; init; }                      // the ticket
    public Guid SourceEntityId { get; init; }                   // the item (PotholeId, ...)
    public SourceEntityType SourceEntityType { get; init; }     // RoadDamage, AssetDamage, ...
    public string Outcome { get; init; } = default!;            // "Completed", "Rejected" or "Cancelled"
}
