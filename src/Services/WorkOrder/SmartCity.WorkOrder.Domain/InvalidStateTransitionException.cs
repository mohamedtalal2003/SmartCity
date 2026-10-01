using SmartCity.Contracts.Enums;

namespace SmartCity.WorkOrder.Domain;

public class InvalidStateTransitionException : InvalidOperationException
{
    public WorkOrderStatus From { get; }
    public WorkOrderStatus To { get; }

    public InvalidStateTransitionException(WorkOrderStatus from, WorkOrderStatus to)
        : base($"Transition from {from} to {to} is not allowed.")
    {
        From = from;
        To = to;
    }
}
