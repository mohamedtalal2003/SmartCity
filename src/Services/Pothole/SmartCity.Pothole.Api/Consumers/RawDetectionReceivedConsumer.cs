using MassTransit;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.Pothole.Application.Detections;

namespace SmartCity.Pothole.Api.Consumers;

/// <summary>Thin adapter: road-damage detections go to the <see cref="HandleDetection"/> use case.</summary>
public sealed class RawDetectionReceivedConsumer : IConsumer<RawDetectionReceived>
{
    private readonly HandleDetection _handleDetection;

    public RawDetectionReceivedConsumer(HandleDetection handleDetection) => _handleDetection = handleDetection;

    public async Task Consume(ConsumeContext<RawDetectionReceived> context)
    {
        // Other detection types belong to Inventory, Violation and Building.
        if (!context.Message.Type.IsRoadDamage())
            return;

        await _handleDetection.HandleAsync(context.Message, context.CancellationToken);
    }
}
