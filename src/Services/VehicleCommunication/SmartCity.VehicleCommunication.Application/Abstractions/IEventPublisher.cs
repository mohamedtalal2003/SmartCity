using SmartCity.Contracts.Events;

namespace SmartCity.VehicleCommunication.Application.Abstractions;

/// <summary>
/// Publishes an event through the outbox. The event leaves only when the repository's save commits, so the
/// row and the event commit together.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : IntegrationEvent;
}
