using Microsoft.Extensions.Logging;
using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Application.Abstractions;

namespace SmartCity.Pothole.Application.WorkOrders;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Use case: Work Order created a ticket for a pothole, or the ticket status changed.
/// The pothole keeps a display-only copy of the status, and closes as Repaired when the ticket is Completed.
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

    public async Task HandleAsync(
        Guid potholeId, Guid workOrderId, WorkOrderStatus status, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var pothole = await _repository.GetByIdAsync(potholeId, ct);
        if (pothole is null)
        {
            // Retrying would not change the result, so log and stop.
            _logger.LogWarning("Pothole {PotholeId} not found for work order {WorkOrderId}, message ignored",
                potholeId, workOrderId);
            return;
        }

        // SKELETON: the contract has no ticket_sequence yet, so the message time orders the updates.
        // Switch to ticket_sequence after the contract PR.
        var sequence = occurredAt.UtcTicks;

        pothole.ApplyRepairStatus(workOrderId, status.ToString(), sequence);
        if (status == WorkOrderStatus.Completed)
            pothole.Close(ClosedReason.Repaired, duplicateOfId: null, occurredAt);

        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Pothole {PotholeId}: work order {WorkOrderId} is {Status}, pothole is {PotholeStatus}",
            potholeId, workOrderId, status, pothole.Status);
    }
}
