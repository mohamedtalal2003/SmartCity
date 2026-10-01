using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using SmartCity.Contracts.Events.Business;
using SmartCity.Notification.Api.Data;
using SmartCity.Notification.Api.Domain;
using SmartCity.Notification.Api.Hubs;

namespace SmartCity.Notification.Api.Consumers;

public sealed class WorkOrderCreatedConsumer : IConsumer<WorkOrderCreated>
{
    private readonly NotificationDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public WorkOrderCreatedConsumer(NotificationDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task Consume(ConsumeContext<WorkOrderCreated> context)
    {
        var msg = context.Message;

        var notification = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            CorrelationId = msg.CorrelationId,
            Title = $"New work order: {msg.TicketNumber}",
            Body = $"{msg.Title} — Priority: {msg.Priority}, District: {msg.District}",
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

        Log.Information("Notification sent for work order {TicketNumber}", msg.TicketNumber);
    }
}
