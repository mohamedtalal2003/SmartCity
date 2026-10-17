using MassTransit;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.VehicleCommunication.Application.Abstractions;

namespace SmartCity.VehicleCommunication.Infrastructure.Messaging;

/// <summary>
/// Publishes telemetry with <see cref="IBus"/>, which goes straight to RabbitMQ. (The scoped IPublishEndpoint would
/// put it in the outbox, which only leaves on a database save, and heartbeats do not save every time.)
/// </summary>
public sealed class BusTelemetryPublisher : ITelemetryPublisher
{
    private readonly IBus _bus;

    public BusTelemetryPublisher(IBus bus) => _bus = bus;

    public Task PublishAsync(VehicleTelemetryReceived telemetry, CancellationToken ct) => _bus.PublishEvent(telemetry, ct);
}
