using MassTransit;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.Pothole.Application.Verifications;

namespace SmartCity.Pothole.Api.Consumers;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>Thin adapter: a verification of a road-damage repair goes to <see cref="RecordVerificationPass"/>.</summary>
public sealed class RepairVerificationObservedConsumer : IConsumer<RepairVerificationObserved>
{
    private readonly RecordVerificationPass _recordVerificationPass;

    public RepairVerificationObservedConsumer(RecordVerificationPass recordVerificationPass) =>
        _recordVerificationPass = recordVerificationPass;

    public async Task Consume(ConsumeContext<RepairVerificationObserved> context)
    {
        var msg = context.Message;
        if (msg.SourceEntityType != SourceEntityType.RoadDamage)
            return;

        var pass = new VerificationPass(msg.FrameId, msg.Result, msg.Confidence, msg.ImageUrl, msg.ObservedAt);
        await _recordVerificationPass.HandleAsync(msg.SourceEntityId, pass, context.CancellationToken);
    }
}
