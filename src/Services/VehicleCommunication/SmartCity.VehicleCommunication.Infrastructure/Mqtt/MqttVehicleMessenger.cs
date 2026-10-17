using System.Text.Json;
using System.Text.Json.Serialization;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Fleet;

namespace SmartCity.VehicleCommunication.Infrastructure.Mqtt;

/// <summary>
/// Sends the server → vehicle messages as retained MQTT messages. JSON is camelCase without null fields, exactly as
/// in docs/design/vehicle/vehicle-protocol.md.
/// </summary>
public sealed class MqttVehicleMessenger : IVehicleMessenger
{
    public const string FleetStatusTopic = "fleet/status";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly MqttConnection _connection;

    public MqttVehicleMessenger(MqttConnection connection) => _connection = connection;

    public Task PublishFleetStatusAsync(FleetStatusMessage status, CancellationToken ct) =>
        _connection.PublishRetainedAsync(FleetStatusTopic, JsonSerializer.Serialize(status, Json), ct);

    public Task PublishConfigAsync(string vehicleId, VehicleConfigMessage config, CancellationToken ct) =>
        _connection.PublishRetainedAsync($"vehicles/{vehicleId}/config", JsonSerializer.Serialize(config, Json), ct);
}
