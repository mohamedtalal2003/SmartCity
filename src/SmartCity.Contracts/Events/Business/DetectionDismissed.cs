using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Business;

/// <summary>
/// Published by Work Order when an officer dismisses a detection as wrong (scenario P7).
/// The item (e.g. the pothole) always exists already, so the message carries its id.
///
/// Subscribers: Pothole (closes the pothole as FalsePositive; the 30-day rule starts).
/// Not published by anyone yet.
/// </summary>
public record DetectionDismissed : IntegrationEvent
{
    public Guid? WorkOrderId { get; init; }                     // the ticket, if one was already open
    public Guid SourceEntityId { get; init; }                   // the item that was dismissed (PotholeId, ...)
    public SourceEntityType SourceEntityType { get; init; }     // RoadDamage, AssetDamage, ...
    public string Reason { get; init; } = default!;             // the officer's reason, free text
    public string DismissedBy { get; init; } = default!;        // officer user id
    public DateTimeOffset DismissedAt { get; init; }            // when it was dismissed (UTC)
}
