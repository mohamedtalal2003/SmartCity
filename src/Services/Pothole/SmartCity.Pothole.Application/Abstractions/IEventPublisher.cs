using SmartCity.Contracts.Events;

namespace SmartCity.Pothole.Application.Abstractions;

/// <summary>
/// The only messaging port. The event is written to the outbox and leaves only when
/// <see cref="IPotholeRepository.SaveChangesAsync"/> commits, so the row and the event commit together.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : IntegrationEvent;
}
