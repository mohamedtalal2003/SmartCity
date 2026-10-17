using SmartCity.Contracts.Events.Ingestion;

namespace SmartCity.VehicleCommunication.Application.Abstractions;

/// <summary>
/// Publishes <see cref="VehicleTelemetryReceived"/> straight to the bus, not through the outbox: no database write
/// goes with each heartbeat, and a lost heartbeat is replaced by the next one 2 seconds later (same as before).
/// </summary>
public interface ITelemetryPublisher
{
    Task PublishAsync(VehicleTelemetryReceived telemetry, CancellationToken ct);
}
