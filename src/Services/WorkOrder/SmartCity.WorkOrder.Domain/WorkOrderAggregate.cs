using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.WorkOrder.Domain;

public class WorkOrderAggregate
{
    private static readonly HashSet<(WorkOrderStatus From, WorkOrderStatus To)> AllowedTransitions = new()
    {
        (WorkOrderStatus.Pending, WorkOrderStatus.Assigned),
        (WorkOrderStatus.Assigned, WorkOrderStatus.EnRoute),
        (WorkOrderStatus.Assigned, WorkOrderStatus.Pending),
        (WorkOrderStatus.EnRoute, WorkOrderStatus.OnSite),
        (WorkOrderStatus.OnSite, WorkOrderStatus.Paused),
        (WorkOrderStatus.OnSite, WorkOrderStatus.Completed),
        (WorkOrderStatus.Paused, WorkOrderStatus.OnSite),
    };

    private static readonly Dictionary<PriorityLevel, TimeSpan> SlaDurations = new()
    {
        [PriorityLevel.Critical] = TimeSpan.FromHours(24),
        [PriorityLevel.High] = TimeSpan.FromHours(48),
        [PriorityLevel.Medium] = TimeSpan.FromDays(5),
        [PriorityLevel.Routine] = TimeSpan.FromDays(10),
    };

    public Guid Id { get; private set; }
    public string TicketNumber { get; private set; } = default!;
    public Guid SourceDetectionId { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public SourceEntityType SourceEntityType { get; private set; }
    public Guid CorrelationId { get; private set; }
    public GeoLocation Location { get; private set; } = default!;
    public District District { get; private set; }
    public WorkOrderCategory Category { get; private set; }
    public PriorityLevel Priority { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public string? AssignedCrewId { get; private set; }
    public decimal? EstimatedCost { get; private set; }
    public DateTimeOffset? SlaDueAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CompletionProofUrl { get; private set; }
    public PauseReason? CurrentPauseReason { get; private set; }
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string CreatedBy { get; private set; } = "system";

    private readonly List<StatusHistoryEntry> _statusHistory = new();
    public IReadOnlyList<StatusHistoryEntry> StatusHistory => _statusHistory.AsReadOnly();

    private readonly List<object> _domainEvents = new();
    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();

    private WorkOrderAggregate() { }

    public static WorkOrderAggregate Create(
        string ticketNumber,
        Guid sourceDetectionId,
        Guid sourceEntityId,
        SourceEntityType sourceEntityType,
        Guid correlationId,
        GeoLocation location,
        District district,
        WorkOrderCategory category,
        PriorityLevel priority,
        string title,
        string description,
        decimal? estimatedCost = null)
    {
        var wo = new WorkOrderAggregate
        {
            Id = Guid.NewGuid(),
            TicketNumber = ticketNumber,
            SourceDetectionId = sourceDetectionId,
            SourceEntityId = sourceEntityId,
            SourceEntityType = sourceEntityType,
            CorrelationId = correlationId,
            Location = location,
            District = district,
            Category = category,
            Priority = priority,
            Status = WorkOrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            SlaDueAt = SlaDurations.TryGetValue(priority, out var dur) ? DateTimeOffset.UtcNow + dur : null,
            EstimatedCost = estimatedCost,
            Title = title,
            Description = description,
        };

        wo._domainEvents.Add(new WorkOrderCreatedDomainEvent(wo));
        return wo;
    }

    public void AssignCrew(string crewId, string by)
    {
        TransitionTo(WorkOrderStatus.Assigned, by);
        AssignedCrewId = crewId;
    }

    public void MarkEnRoute(string by) => TransitionTo(WorkOrderStatus.EnRoute, by);
    public void MarkOnSite(string by) => TransitionTo(WorkOrderStatus.OnSite, by);

    public void Pause(PauseReason reason, string by)
    {
        TransitionTo(WorkOrderStatus.Paused, by, notes: reason.ToString());
        CurrentPauseReason = reason;
    }

    public void Resume(string by)
    {
        TransitionTo(WorkOrderStatus.OnSite, by);
        CurrentPauseReason = null;
    }

    public void Complete(string proofUrl, string by)
    {
        TransitionTo(WorkOrderStatus.Completed, by);
        CompletionProofUrl = proofUrl;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void AttachCostEstimate(decimal amount)
    {
        EstimatedCost = amount;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void TransitionTo(WorkOrderStatus newStatus, string by, string? notes = null)
    {
        if (!AllowedTransitions.Contains((Status, newStatus)))
            throw new InvalidStateTransitionException(Status, newStatus);

        var entry = new StatusHistoryEntry(Status, newStatus, by, notes);
        _statusHistory.Add(entry);

        var previousStatus = Status;
        Status = newStatus;

        _domainEvents.Add(new WorkOrderStatusChangedDomainEvent(this, previousStatus, entry));
    }
}

public record WorkOrderCreatedDomainEvent(WorkOrderAggregate WorkOrder);
public record WorkOrderStatusChangedDomainEvent(
    WorkOrderAggregate WorkOrder,
    WorkOrderStatus PreviousStatus,
    StatusHistoryEntry HistoryEntry);
