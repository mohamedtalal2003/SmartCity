using MassTransit;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Ingestion;

namespace SmartCity.Inventory.Api.Consumers;

public sealed class RawDetectionReceivedConsumer : IConsumer<RawDetectionReceived>
{
    private readonly ILogger<RawDetectionReceivedConsumer> _logger;

    public RawDetectionReceivedConsumer(ILogger<RawDetectionReceivedConsumer> logger) => _logger = logger;

    public Task Consume(ConsumeContext<RawDetectionReceived> context)
    {
        var msg = context.Message;

        if (!msg.Type.IsAsset())
            return Task.CompletedTask;

        // SKELETON: real version persists asset state and publishes AssetAddedOrUpdated via outbox.
        _logger.LogInformation("Received {Type} for detection {DetectionId} — not implemented",
            msg.Type, msg.DetectionId);

        return Task.CompletedTask;
    }
}
