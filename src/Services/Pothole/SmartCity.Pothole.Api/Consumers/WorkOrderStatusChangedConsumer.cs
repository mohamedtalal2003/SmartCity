using MassTransit;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.Pothole.Application.WorkOrders;

namespace SmartCity.Pothole.Api.Consumers;

/// <summary>Thin adapter: a ticket status change for a pothole goes to <see cref="ApplyWorkOrderUpdate"/>.</summary>
public sealed class WorkOrderStatusChangedConsumer : IConsumer<WorkOrderStatusChanged>
{
    private readonly ApplyWorkOrderUpdate _applyWorkOrderUpdate;

    public WorkOrderStatusChangedConsumer(ApplyWorkOrderUpdate applyWorkOrderUpdate) => _applyWorkOrderUpdate = applyWorkOrderUpdate;

    public async Task Consume(ConsumeContext<WorkOrderStatusChanged> context)
    {
        var msg = context.Message;
        if (msg.SourceEntityType != SourceEntityType.RoadDamage)
            return;

        await _applyWorkOrderUpdate.HandleAsync(
            new WorkOrderUpdate(
                msg.SourceEntityId, msg.WorkOrderId, msg.NewStatus, msg.TicketSequence, msg.OccurredAt,
                msg.CancelReason, msg.DuplicateOfEntityId),
            context.CancellationToken);
    }
}
