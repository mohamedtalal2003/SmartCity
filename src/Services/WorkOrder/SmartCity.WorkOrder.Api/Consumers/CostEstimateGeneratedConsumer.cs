using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SmartCity.Contracts.Events.Business;
using SmartCity.WorkOrder.Api.Data;
using SmartCity.WorkOrder.Api.Domain;

namespace SmartCity.WorkOrder.Api.Consumers;

public sealed class CostEstimateGeneratedConsumer : IConsumer<CostEstimateGenerated>
{
    private readonly WorkOrderDbContext _db;

    public CostEstimateGeneratedConsumer(WorkOrderDbContext db) => _db = db;

    public async Task Consume(ConsumeContext<CostEstimateGenerated> context)
    {
        var msg = context.Message;

        var wo = await _db.WorkOrders
            .FirstOrDefaultAsync(w => w.SourceEntityId == msg.SourceEntityId, context.CancellationToken);

        if (wo is not null)
        {
            wo.AttachCostEstimate(msg.TotalEstimatedCost);
            await _db.SaveChangesAsync(context.CancellationToken);
            Log.Information("Attached cost {Cost} to work order {Id}", msg.TotalEstimatedCost, wo.Id);
            return;
        }

        if (await _db.PendingCostEstimates.AnyAsync(
                p => p.SourceEntityId == msg.SourceEntityId, context.CancellationToken))
            return;

        _db.PendingCostEstimates.Add(new PendingCostEstimate
        {
            Id = Guid.NewGuid(),
            SourceEntityId = msg.SourceEntityId,
            TotalEstimatedCost = msg.TotalEstimatedCost,
            ReceivedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(context.CancellationToken);

        Log.Information("Stored pending cost estimate for entity {EntityId}, work order not yet created",
            msg.SourceEntityId);
    }
}
