using MassTransit;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.Pothole.Application.WorkOrders;

namespace SmartCity.Pothole.Api.Consumers;

/// <summary>Thin adapter: a dismissed road-damage detection goes to <see cref="DismissDetection"/>.</summary>
public sealed class DetectionDismissedConsumer : IConsumer<DetectionDismissed>
{
    private readonly DismissDetection _dismissDetection;

    public DetectionDismissedConsumer(DismissDetection dismissDetection) => _dismissDetection = dismissDetection;

    public async Task Consume(ConsumeContext<DetectionDismissed> context)
    {
        var msg = context.Message;
        if (msg.SourceEntityType != SourceEntityType.RoadDamage)
            return;

        await _dismissDetection.HandleAsync(msg.SourceEntityId, msg.DismissedAt, context.CancellationToken);
    }
}
