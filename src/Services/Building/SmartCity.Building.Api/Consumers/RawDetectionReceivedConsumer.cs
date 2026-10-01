using MassTransit;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Ingestion;

namespace SmartCity.Building.Api.Consumers;

public sealed class RawDetectionReceivedConsumer : IConsumer<RawDetectionReceived>
{
    private readonly ILogger<RawDetectionReceivedConsumer> _logger;

    public RawDetectionReceivedConsumer(ILogger<RawDetectionReceivedConsumer> logger) => _logger = logger;

    public Task Consume(ConsumeContext<RawDetectionReceived> context)
    {
        var msg = context.Message;

        if (!msg.Type.IsBuilding())
            return Task.CompletedTask;

        // SKELETON: real version persists anomaly record and publishes ConstructionAnomalyDetected via outbox.
        _logger.LogInformation("Received {Type} for detection {DetectionId} — not implemented",
            msg.Type, msg.DetectionId);

        return Task.CompletedTask;
    }
}
