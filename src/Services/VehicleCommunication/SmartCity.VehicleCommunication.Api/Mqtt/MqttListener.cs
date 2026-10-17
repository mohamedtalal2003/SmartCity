using System.Text.Json;
using MQTTnet.Client;
using MQTTnet.Protocol;
using SmartCity.VehicleCommunication.Application.Connections;
using SmartCity.VehicleCommunication.Application.Fleet;
using SmartCity.VehicleCommunication.Infrastructure.Mqtt;

namespace SmartCity.VehicleCommunication.Api.Mqtt;

/// <summary>
/// Receives the small vehicle messages (MQTT): vehicles/{id}/telemetry (V7) and vehicles/{id}/status (V6, V18).
/// Thin adapter: topic + JSON → use case. Frames do not come over MQTT any more; they use gRPC.
/// Shared subscriptions ($share/vehiclecomm/...), so with several instances each message goes to one of them.
/// </summary>
public sealed class MqttListener : BackgroundService
{
    private const string TelemetryFilter = "$share/vehiclecomm/vehicles/+/telemetry";
    private const string StatusFilter = "$share/vehiclecomm/vehicles/+/status";

    private readonly MqttConnection _connection;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MqttListener> _logger;

    public MqttListener(MqttConnection connection, IServiceScopeFactory scopes, ILogger<MqttListener> logger)
    {
        _connection = connection;
        _scopes = scopes;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection.Client.ApplicationMessageReceivedAsync += e => OnMessageAsync(e, stoppingToken);
        return _connection.KeepConnectedAsync(OnConnectedAsync, stoppingToken);
    }

    // After every (re)connect: subscribe, then publish fleet/status and the configs again (retained).
    private async Task OnConnectedAsync(CancellationToken ct)
    {
        await _connection.Client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(TelemetryFilter, MqttQualityOfServiceLevel.AtLeastOnce)
            .WithTopicFilter(StatusFilter, MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);
        _logger.LogInformation("Subscribed to vehicle telemetry and status");

        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AnnounceToFleet>().HandleAsync(ct);
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e, CancellationToken ct)
    {
        var topic = e.ApplicationMessage.Topic;
        try
        {
            // vehicles/{id}/telemetry or vehicles/{id}/status
            var parts = topic.Split('/');
            if (parts.Length != 3 || parts[0] != "vehicles")
                return;

            var vehicleId = parts[1];
            var payload = e.ApplicationMessage.PayloadSegment;
            using var scope = _scopes.CreateScope();

            switch (parts[2])
            {
                case "telemetry":
                    if (TelemetryJson.TryParse(vehicleId, payload, out var telemetry))
                        await scope.ServiceProvider.GetRequiredService<RecordTelemetry>().HandleAsync(telemetry, ct);
                    else
                        _logger.LogWarning("Bad telemetry JSON from {VehicleId} ignored", vehicleId);
                    break;

                case "status":
                    var state = StateOf(payload);
                    if (state is "online" or "offline")
                        await scope.ServiceProvider.GetRequiredService<UpdateConnection>().HandleAsync(vehicleId, state == "online", ct);
                    else
                        _logger.LogWarning("Bad status message from {VehicleId} ignored", vehicleId);
                    break;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error handling MQTT message on {Topic}", topic);
        }
    }

    // {"state":"online"} → "online"; anything else → null.
    private static string? StateOf(ArraySegment<byte> payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.TryGetProperty("state", out var state) ? state.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
