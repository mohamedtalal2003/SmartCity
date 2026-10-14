using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Business;

/// <summary>
/// Published by Work Order on every lifecycle transition
/// (the aggregate's TransitionTo method raises it).
/// Subscribers: Notification, Pothole (display-only repair status; closes the pothole on
/// Completed or Cancelled).
/// </summary>
public record WorkOrderStatusChanged : IntegrationEvent
{
    public Guid WorkOrderId { get; init; }
    public string TicketNumber { get; init; } = default!;
    public long TicketSequence { get; init; }               // 1 at creation, +1 on every change of this ticket; orders its messages
    public Guid SourceDetectionId { get; init; }
    public Guid SourceEntityId { get; init; }
    public SourceEntityType SourceEntityType { get; init; }

    public WorkOrderStatus PreviousStatus { get; init; }
    public WorkOrderStatus NewStatus { get; init; }
    public string ChangedBy { get; init; } = default!;      // user id or "system"

    public PriorityLevel Priority { get; init; }
    public string? AssignedCrewId { get; init; }
    public PauseReason? PauseReason { get; init; }          // set only when NewStatus = Paused
    public CancelReason? CancelReason { get; init; }        // set only when NewStatus = Cancelled
    public Guid? DuplicateOfEntityId { get; init; }         // the item that stays; set only when CancelReason = Duplicate
    public string? Notes { get; init; }

    public bool SlaBreached { get; init; }
    public TimeSpan TimeInPreviousStatus { get; init; }
}
