using MassTransit;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.Pothole.Application.WorkOrders;

namespace SmartCity.Pothole.Api.Consumers;

/// <summary>Thin adapter: a new ticket for a pothole goes to <see cref="ApplyWorkOrderUpdate"/> as Pending.</summary>
public sealed class WorkOrderCreatedConsumer : IConsumer<WorkOrderCreated>
{
    private readonly ApplyWorkOrderUpdate _applyWorkOrderUpdate;

    public WorkOrderCreatedConsumer(ApplyWorkOrderUpdate applyWorkOrderUpdate) => _applyWorkOrderUpdate = applyWorkOrderUpdate;

    public async Task Consume(ConsumeContext<WorkOrderCreated> context)
    {
        var msg = context.Message;
        if (msg.SourceEntityType != SourceEntityType.RoadDamage)
            return;

        await _applyWorkOrderUpdate.HandleAsync(
            new WorkOrderUpdate(msg.SourceEntityId, msg.WorkOrderId, WorkOrderStatus.Pending, msg.TicketSequence, msg.OccurredAt),
            context.CancellationToken);
    }
}
