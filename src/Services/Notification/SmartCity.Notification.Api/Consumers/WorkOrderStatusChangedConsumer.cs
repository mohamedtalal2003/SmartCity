using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using SmartCity.Contracts.Events.Business;
using SmartCity.Notification.Api.Data;
using SmartCity.Notification.Api.Domain;
using SmartCity.Notification.Api.Hubs;

namespace SmartCity.Notification.Api.Consumers;

public sealed class WorkOrderStatusChangedConsumer : IConsumer<WorkOrderStatusChanged>
{
    private readonly NotificationDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public WorkOrderStatusChangedConsumer(NotificationDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task Consume(ConsumeContext<WorkOrderStatusChanged> context)
    {
        var msg = context.Message;

        var notification = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            CorrelationId = msg.CorrelationId,
            Title = $"Work order {msg.TicketNumber}: {msg.PreviousStatus} → {msg.NewStatus}",
            Body = $"Changed by {msg.ChangedBy}",
            RelatedEntityId = msg.WorkOrderId,
            RelatedEntityType = msg.SourceEntityType,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(context.CancellationToken);

        await _hub.Clients.All.SendAsync("NotificationReceived", new
        {
            notification.Id,
            notification.Title,
            notification.Body,
            notification.RelatedEntityId,
            notification.CreatedAt,
        }, context.CancellationToken);

        Log.Information("Notification sent for status change {TicketNumber}: {From} -> {To}",
            msg.TicketNumber, msg.PreviousStatus, msg.NewStatus);
    }
}
