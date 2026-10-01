using System.Collections.Concurrent;
using System.Reflection;
using MassTransit;
using SmartCity.Contracts.Events;

namespace SmartCity.BuildingBlocks;

/// <summary>
/// The only two ways services publish. Both set MassTransit's MessageId = EventId, which is what the
/// inbox de-duplicates on.
/// </summary>
public static class PublishExtensions
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> SourceDetectionIdProperties = new();

    /// <summary>
    /// Publish an event caused by the consumed one. Enforces the trace rules: the follow-up must carry the
    /// consumed event's CorrelationId unchanged, and must set SourceDetectionId when its type has one.
    /// Inside a consumer with the outbox, the publish commits with the consumer's SaveChanges.
    /// </summary>
    public static Task PublishFollowUp<TConsumed, TFollowUp>(
        this ConsumeContext<TConsumed> context, TFollowUp followUp, CancellationToken cancellationToken = default)
        where TConsumed : IntegrationEvent
        where TFollowUp : IntegrationEvent
    {
        if (followUp.CorrelationId != context.Message.CorrelationId)
            throw new InvalidOperationException(
                $"{typeof(TFollowUp).Name}.CorrelationId must be copied unchanged from {typeof(TConsumed).Name} " +
                $"({context.Message.CorrelationId}), but was {followUp.CorrelationId}.");

        var sourceDetectionId = SourceDetectionIdProperties.GetOrAdd(typeof(TFollowUp),
            t => t.GetProperty("SourceDetectionId", typeof(Guid)));
        if (sourceDetectionId is not null && (Guid)sourceDetectionId.GetValue(followUp)! == Guid.Empty)
            throw new InvalidOperationException($"{typeof(TFollowUp).Name}.SourceDetectionId must be set.");

        return context.Publish(followUp, p => p.MessageId = followUp.EventId, cancellationToken);
    }

    /// <summary>
    /// Publish an event that starts a chain or follows a gRPC write (no consumed message).
    /// With the bus outbox, it commits with the next SaveChanges on the service's DbContext.
    /// </summary>
    public static Task PublishEvent<TEvent>(
        this IPublishEndpoint publishEndpoint, TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent =>
        publishEndpoint.Publish(integrationEvent, p => p.MessageId = integrationEvent.EventId, cancellationToken);
}
