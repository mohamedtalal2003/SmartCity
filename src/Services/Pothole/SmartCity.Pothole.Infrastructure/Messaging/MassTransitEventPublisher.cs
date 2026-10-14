using MassTransit;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Events;
using SmartCity.Pothole.Application.Abstractions;

namespace SmartCity.Pothole.Infrastructure.Messaging;

/// <summary>
/// Publishes through MassTransit with MessageId = EventId (so the receivers' inbox can de-duplicate).
/// Inside a consumer, the scoped <see cref="IPublishEndpoint"/> is the consume context, so the event goes
/// into the EF Core outbox and leaves only when the use case's SaveChanges commits.
/// </summary>
public sealed class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventPublisher(IPublishEndpoint publishEndpoint) => _publishEndpoint = publishEndpoint;

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : IntegrationEvent =>
        _publishEndpoint.PublishEvent(integrationEvent, ct);
}
