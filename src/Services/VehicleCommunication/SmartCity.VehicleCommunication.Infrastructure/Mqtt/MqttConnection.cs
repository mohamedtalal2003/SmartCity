using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace SmartCity.VehicleCommunication.Infrastructure.Mqtt;

/// <summary>The "Mqtt" config section.</summary>
public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
}

/// <summary>
/// The service's one connection to EMQX, used to receive (telemetry, status) and to send (fleet/status, config).
/// <see cref="KeepConnectedAsync"/> connects, and connects again after a drop.
/// SKELETON: no TLS and no credentials. The real version connects with the service's own certificate (mTLS).
/// </summary>
public sealed class MqttConnection : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly MqttOptions _options;
    private readonly ILogger<MqttConnection> _logger;

    public MqttConnection(IOptions<MqttOptions> options, ILogger<MqttConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
        Client = new MqttFactory().CreateMqttClient();
    }

    /// <summary>The MQTTnet client. Attach to <c>ApplicationMessageReceivedAsync</c> to receive messages.</summary>
    public IMqttClient Client { get; }

    /// <summary>
    /// Stays connected until <paramref name="ct"/> is cancelled. After every (re)connect it calls
    /// <paramref name="onConnected"/> (to subscribe and publish the retained state again).
    /// </summary>
    public async Task KeepConnectedAsync(Func<CancellationToken, Task> onConnected, CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId($"vehiclecomm-{Environment.MachineName}-{Guid.NewGuid():N}"[..30])
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .Build();

        while (!ct.IsCancellationRequested)
        {
            if (!Client.IsConnected)
            {
                try
                {
                    await Client.ConnectAsync(options, ct);
                    _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", _options.Host, _options.Port);
                    await onConnected(ct);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("MQTT connection failed, retrying in {Seconds} s: {Error}", ReconnectDelay.TotalSeconds, e.Message);
                }
            }

            try { await Task.Delay(ReconnectDelay, ct); }
            catch (OperationCanceledException) { break; }
        }

        if (Client.IsConnected)
            await Client.DisconnectAsync();
    }

    /// <summary>Publishes a retained JSON message with QoS 1. Throws when not connected.</summary>
    public Task PublishRetainedAsync(string topic, string json, CancellationToken ct) =>
        Client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag()
            .WithContentType("application/json")
            .Build(), ct);

    public void Dispose() => Client.Dispose();
}
