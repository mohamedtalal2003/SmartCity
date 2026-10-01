using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Detection;
using SmartCity.Notification.Api.Data;
using SmartCity.Notification.Api.Domain;
using SmartCity.Notification.Api.Hubs;

namespace SmartCity.Notification.Api.Consumers;

public sealed class PotholeSavedConsumer : IConsumer<PotholeSaved>
{
    private readonly NotificationDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public PotholeSavedConsumer(NotificationDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task Consume(ConsumeContext<PotholeSaved> context)
    {
        var msg = context.Message;
        if (!msg.IsNewPothole) return;

        var notification = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            CorrelationId = msg.CorrelationId,
            Title = $"New pothole detected — {msg.District}",
            Body = $"Severity: {msg.Severity}, Type: {msg.DetectionType}, Confidence: {msg.Confidence:P0}",
            RelatedEntityId = msg.PotholeId,
            RelatedEntityType = SourceEntityType.RoadDamage,
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

        Log.Information("Notification sent for pothole {PotholeId}", msg.PotholeId);
    }
}
