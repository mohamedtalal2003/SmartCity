using SmartCity.Contracts.Enums;

namespace SmartCity.WorkOrder.Domain;

public class StatusHistoryEntry
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public WorkOrderStatus From { get; private set; }
    public WorkOrderStatus To { get; private set; }
    public string ChangedBy { get; private set; } = default!;
    public DateTimeOffset ChangedAt { get; private set; }
    public string? Notes { get; private set; }

    private StatusHistoryEntry() { }

    internal StatusHistoryEntry(WorkOrderStatus from, WorkOrderStatus to, string changedBy, string? notes)
    {
        From = from;
        To = to;
        ChangedBy = changedBy;
        ChangedAt = DateTimeOffset.UtcNow;
        Notes = notes;
    }
}
