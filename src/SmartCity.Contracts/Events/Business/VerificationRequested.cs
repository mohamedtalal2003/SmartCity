using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Business;

/// <summary>
/// Published by Work Order when a ticket enters AwaitingReview (the crew sent the repair proof).
/// It asks the vehicles to photograph this place again so the AI can confirm the repair (scenario P10).
///
/// Subscribers: Vehicle Communication (keeps the list of places to check and sends it to the vehicles).
/// Not published by anyone yet.
/// </summary>
public record VerificationRequested : IntegrationEvent
{
    public Guid WorkOrderId { get; init; }                      // the ticket waiting for confirmation
    public string TicketNumber { get; init; } = default!;       // "WO-2026-000123"
    public Guid SourceEntityId { get; init; }                   // the repaired item (PotholeId, ...)
    public SourceEntityType SourceEntityType { get; init; }     // RoadDamage, AssetDamage, ...
    public GeoLocation Location { get; init; } = default!;      // where the vehicles should take the photo
    public DateTimeOffset ProofSubmittedAt { get; init; }       // photos taken before this time do not count
}
