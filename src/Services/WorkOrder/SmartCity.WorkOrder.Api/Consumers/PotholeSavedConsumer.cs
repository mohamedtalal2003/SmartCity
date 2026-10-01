using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.Contracts.Events.Detection;
using SmartCity.WorkOrder.Api.Data;
using SmartCity.WorkOrder.Domain;

namespace SmartCity.WorkOrder.Api.Consumers;

public sealed class PotholeSavedConsumer : IConsumer<PotholeSaved>
{
    private readonly WorkOrderDbContext _db;

    public PotholeSavedConsumer(WorkOrderDbContext db) => _db = db;

    public async Task Consume(ConsumeContext<PotholeSaved> context)
    {
        var msg = context.Message;
        if (!msg.IsNewPothole) return;

        if (await _db.WorkOrders.AnyAsync(w => w.SourceEntityId == msg.PotholeId, context.CancellationToken))
        {
            Log.Information("Work order already exists for pothole {PotholeId}, skipping", msg.PotholeId);
            return;
        }

        var nextVal = await GetNextTicketNumber(context.CancellationToken);

        var pending = await _db.PendingCostEstimates
            .FirstOrDefaultAsync(p => p.SourceEntityId == msg.PotholeId, context.CancellationToken);

        var priority = MapPriority(msg.Severity);

        var wo = WorkOrderAggregate.Create(
            ticketNumber: nextVal,
            sourceDetectionId: msg.SourceDetectionId,
            sourceEntityId: msg.PotholeId,
            sourceEntityType: SourceEntityType.RoadDamage,
            correlationId: msg.CorrelationId,
            location: msg.Location,
            district: msg.District,
            category: WorkOrderCategory.RoadRepair,
            priority: priority,
            title: $"{msg.DetectionType} — {msg.District}",
            description: $"Severity {msg.Severity}, confidence {msg.Confidence:P0}",
            estimatedCost: pending?.TotalEstimatedCost);

        _db.WorkOrders.Add(wo);

        if (pending is not null)
            _db.PendingCostEstimates.Remove(pending);

        foreach (var evt in wo.DomainEvents)
        {
            if (evt is WorkOrderCreatedDomainEvent created)
            {
                var e = new WorkOrderCreated
                {
                    CorrelationId = msg.CorrelationId,
                    WorkOrderId = created.WorkOrder.Id,
                    TicketNumber = created.WorkOrder.TicketNumber,
                    SourceDetectionId = msg.SourceDetectionId,
                    SourceEntityId = msg.PotholeId,
                    SourceEntityType = SourceEntityType.RoadDamage,
                    Location = msg.Location,
                    District = msg.District,
                    Category = WorkOrderCategory.RoadRepair,
                    Priority = priority,
                    Status = WorkOrderStatus.Pending,
                    Title = wo.Title,
                    Description = wo.Description,
                    SlaDueAt = wo.SlaDueAt,
                    EstimatedCost = wo.EstimatedCost,
                };
                await context.PublishFollowUp(e, context.CancellationToken);
            }
        }

        wo.ClearDomainEvents();
        await _db.SaveChangesAsync(context.CancellationToken);

        Log.Information("Created work order {TicketNumber} for pothole {PotholeId}, cost={Cost}",
            wo.TicketNumber, msg.PotholeId, wo.EstimatedCost);
    }

    private async Task<string> GetNextTicketNumber(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT nextval('work_order_ticket_seq')";
        var seq = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return $"WO-{DateTime.UtcNow.Year}-{seq:D6}";
    }

    // SKELETON: real version may use the suggested priority from the cost estimate or an officer override.
    private static PriorityLevel MapPriority(SeverityLevel severity) => severity switch
    {
        SeverityLevel.Critical => PriorityLevel.Critical,
        SeverityLevel.High => PriorityLevel.High,
        SeverityLevel.Medium => PriorityLevel.Medium,
        _ => PriorityLevel.Routine,
    };
}
