using Microsoft.Extensions.Logging;
using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Application.Abstractions;

namespace SmartCity.Pothole.Application.WorkOrders;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>What Pothole needs from <c>WorkOrderCreated</c> and <c>WorkOrderStatusChanged</c>.</summary>
public sealed record WorkOrderUpdate(
    Guid PotholeId,
    Guid WorkOrderId,
    WorkOrderStatus Status,
    long TicketSequence,
    DateTimeOffset OccurredAt,
    CancelReason? CancelReason = null,
    Guid? DuplicateOfId = null);

/// <summary>
/// Use case: Work Order created a ticket for a pothole, or the ticket status changed (P6).
/// The pothole keeps a display-only copy of the status. It closes as Repaired when the ticket is
/// Completed, and with the cancel reason when the ticket is Cancelled.
/// </summary>
public sealed class ApplyWorkOrderUpdate
{
    private readonly IPotholeRepository _repository;
    private readonly ILogger<ApplyWorkOrderUpdate> _logger;

    public ApplyWorkOrderUpdate(IPotholeRepository repository, ILogger<ApplyWorkOrderUpdate> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task HandleAsync(WorkOrderUpdate update, CancellationToken ct)
    {
        var pothole = await _repository.GetByIdAsync(update.PotholeId, ct);
        if (pothole is null)
        {
            // Retrying would not change the result, so log and stop.
            _logger.LogWarning("Pothole {PotholeId} not found for work order {WorkOrderId}, message ignored",
                update.PotholeId, update.WorkOrderId);
            return;
        }

        // SKELETON: Work Order still sends TicketSequence = 0, so then the message time orders the updates.
        // Once Work Order fills TicketSequence, only the first branch is used.
        var sequence = update.TicketSequence > 0 ? update.TicketSequence : update.OccurredAt.UtcTicks;

        // The closing message first writes the last repair status, then closes.
        pothole.ApplyRepairStatus(update.WorkOrderId, update.Status.ToString(), sequence);
        if (update.Status == WorkOrderStatus.Completed)
            pothole.Close(ClosedReason.Repaired, duplicateOfId: null, update.OccurredAt);
        else if (update.Status == WorkOrderStatus.Cancelled)
        {
            var (reason, duplicateOfId) = CloseReasonFor(update);
            pothole.Close(reason, duplicateOfId, update.OccurredAt);
        }

        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Pothole {PotholeId}: work order {WorkOrderId} is {Status}, pothole is {PotholeStatus} ({ClosedReason})",
            update.PotholeId, update.WorkOrderId, update.Status, pothole.Status, pothole.ClosedReason);
    }

    private (ClosedReason Reason, Guid? DuplicateOfId) CloseReasonFor(WorkOrderUpdate update)
    {
        switch (update.CancelReason)
        {
            case CancelReason.FalsePositive:
                return (ClosedReason.FalsePositive, null);
            case CancelReason.ResolvedOtherwise:
                return (ClosedReason.ResolvedOtherwise, null);
            case CancelReason.Duplicate when update.DuplicateOfId is not null && update.DuplicateOfId != update.PotholeId:
                return (ClosedReason.Duplicate, update.DuplicateOfId);
            case CancelReason.Duplicate:
                // A retry would get the same message, so close it anyway and say so.
                _logger.LogWarning("Work order {WorkOrderId} was cancelled as Duplicate without a valid other pothole id; " +
                    "pothole {PotholeId} is closed as ResolvedOtherwise", update.WorkOrderId, update.PotholeId);
                return (ClosedReason.ResolvedOtherwise, null);
            default:
                _logger.LogWarning("Work order {WorkOrderId} was cancelled without a reason; " +
                    "pothole {PotholeId} is closed as ResolvedOtherwise", update.WorkOrderId, update.PotholeId);
                return (ClosedReason.ResolvedOtherwise, null);
        }
    }
}
