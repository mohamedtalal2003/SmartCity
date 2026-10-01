using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SmartCity.Contracts.Events.Business;
using SmartCity.WorkOrder.Api.Data;

namespace SmartCity.WorkOrder.Api.Consumers;

// SKELETON: real version replaces this with an event-sourced saga or a process manager
// that coordinates PotholeSaved and CostEstimateGenerated without relying on outbox
// delivery timing. This consumer exists because the MassTransit EF Core transactional
// outbox holds each consumer's transaction open until it returns, so two concurrent
// consumers cannot see each other's uncommitted writes. The WorkOrderCreated event is
// delivered via the outbox *after* PotholeSaved commits; by then CostEstimateGenerated
// has also committed its pending record, so this consumer can reconcile them.
public sealed class WorkOrderCostReconciliationConsumer : IConsumer<WorkOrderCreated>
{
    private readonly WorkOrderDbContext _db;

    public WorkOrderCostReconciliationConsumer(WorkOrderDbContext db) => _db = db;

    public async Task Consume(ConsumeContext<WorkOrderCreated> context)
    {
        var msg = context.Message;
        if (msg.EstimatedCost is not null && msg.EstimatedCost > 0) return;

        var pending = await _db.PendingCostEstimates
            .FirstOrDefaultAsync(p => p.SourceEntityId == msg.SourceEntityId, context.CancellationToken);
        if (pending is null) return;

        var wo = await _db.WorkOrders
            .FirstOrDefaultAsync(w => w.Id == msg.WorkOrderId, context.CancellationToken);
        if (wo is null || wo.EstimatedCost is not null) return;

        wo.AttachCostEstimate(pending.TotalEstimatedCost);
        _db.PendingCostEstimates.Remove(pending);
        await _db.SaveChangesAsync(context.CancellationToken);

        Log.Information("Reconciled: attached cost {Cost} to work order {TicketNumber}",
            pending.TotalEstimatedCost, wo.TicketNumber);
    }
}
