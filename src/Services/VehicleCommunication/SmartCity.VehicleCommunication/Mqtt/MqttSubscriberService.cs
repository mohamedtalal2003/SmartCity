using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using MassTransit;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Events.Ingestion;
using StackExchange.Redis;

namespace SmartCity.VehicleCommunication.Mqtt;

public sealed class MqttSubscriberService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MqttSubscriberService> _logger;
    private readonly IConfiguration _config;
    private IMqttClient? _client;

    public MqttSubscriberService(IServiceProvider services, ILogger<MqttSubscriberService> logger, IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        var mqttHost = _config["Mqtt:Host"] ?? "localhost";
        var mqttPort = _config.GetValue("Mqtt:Port", 1883);

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(mqttHost, mqttPort)
            .WithClientId($"vehiclecomm-{Environment.MachineName}-{Guid.NewGuid():N}"[..30])
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceived;
        _client.DisconnectedAsync += async e =>
        {
            if (stoppingToken.IsCancellationRequested) return;
            _logger.LogWarning("MQTT disconnected: {Reason}. Reconnecting in 3s...", e.Reason);
            await Task.Delay(3000, stoppingToken);
            try { await _client.ConnectAsync(options, stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "MQTT reconnect failed"); }
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _client.ConnectAsync(options, stoppingToken);
                _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", mqttHost, mqttPort);

                await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter("$share/vehiclecomm/vehicles/+/frames", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithTopicFilter("$share/vehiclecomm/vehicles/+/telemetry", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(), stoppingToken);

                _logger.LogInformation("Subscribed to vehicle frames and telemetry topics");
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "MQTT connection failed, retrying in 3s...");
                await Task.Delay(3000, stoppingToken);
            }
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var segments = topic.Split('/');
            if (segments.Length < 3) return;

            var vehicleId = segments[1];
            var messageType = segments[2];

            if (messageType == "frames")
                await HandleFrame(vehicleId, e.ApplicationMessage);
            else if (messageType == "telemetry")
                await HandleTelemetry(vehicleId, e.ApplicationMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message on topic {Topic}", e.ApplicationMessage.Topic);
        }
    }

    private async Task HandleFrame(string vehicleId, MqttApplicationMessage msg)
    {
        if (!IsValidVehicleId(vehicleId))
        {
            _logger.LogWarning("Invalid vehicleId format: {VehicleId}", vehicleId);
            return;
        }

        var props = msg.UserProperties?.ToDictionary(p => p.Name, p => p.Value)
            ?? new Dictionary<string, string>();

        if (!props.TryGetValue("frameId", out var frameIdStr) ||
            !props.TryGetValue("capturedAt", out var capturedAtStr) ||
            !props.TryGetValue("lat", out var latStr) ||
            !props.TryGetValue("lon", out var lonStr))
        {
            _logger.LogWarning("Frame from {VehicleId} missing required user properties", vehicleId);
            return;
        }

        if (!Guid.TryParse(frameIdStr, out var frameId))
        {
            _logger.LogWarning("Frame from {VehicleId} has invalid frameId: {FrameId}", vehicleId, frameIdStr);
            return;
        }

        if (!double.TryParse(latStr, out var lat) || !double.TryParse(lonStr, out var lon))
        {
            _logger.LogWarning("Frame {FrameId} from {VehicleId} has invalid coordinates", frameId, vehicleId);
            return;
        }

        // Konya bounding box validation
        if (lat < 37.6 || lat > 38.2 || lon < 32.2 || lon > 32.8)
        {
            _logger.LogWarning("Frame {FrameId} from {VehicleId} outside Konya bounding box: lat={Lat}, lon={Lon}",
                frameId, vehicleId, lat, lon);
            return;
        }

        DateTimeOffset.TryParse(capturedAtStr, out var capturedAt);
        props.TryGetValue("accuracy", out var accStr);
        props.TryGetValue("heading", out var headStr);
        props.TryGetValue("speed", out var speedStr);
        props.TryGetValue("width", out var widthStr);
        props.TryGetValue("height", out var heightStr);
        props.TryGetValue("contentType", out var contentType);

        double.TryParse(accStr, out var accuracy);
        double.TryParse(headStr, out var heading);
        double.TryParse(speedStr, out var speed);
        int.TryParse(widthStr, out var width);
        int.TryParse(heightStr, out var height);

        var dateKey = capturedAt.UtcDateTime.ToString("yyyy-MM-dd");
        var s3Key = $"{vehicleId}/{dateKey}/{frameId}.jpg";

        using var scope = _services.CreateScope();
        var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
        var s3Options = scope.ServiceProvider.GetRequiredService<IOptions<S3Options>>().Value;
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        using var stream = new MemoryStream(msg.PayloadSegment.ToArray());
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "frames-temp",
            Key = s3Key,
            InputStream = stream,
            ContentType = contentType ?? "image/jpeg",
        });

        var imageUrl = $"{s3Options.PublicBaseUrl}/frames-temp/{s3Key}";

        var frameReceived = new FrameReceived
        {
            FrameId = frameId,
            CorrelationId = frameId,
            VehicleId = vehicleId,
            CapturedAt = capturedAt,
            Location = new GeoLocation
            {
                Latitude = lat,
                Longitude = lon,
                AccuracyMeters = accuracy,
                Heading = heading,
            },
            SpeedKmh = speed,
            ImageUrl = imageUrl,
            ContentType = contentType ?? "image/jpeg",
            WidthPx = width,
            HeightPx = height,
        };

        await publishEndpoint.PublishEvent(frameReceived);
        _logger.LogInformation("Frame {FrameId} from {VehicleId} uploaded to {ImageUrl} and published",
            frameId, vehicleId, imageUrl);
    }

    private async Task HandleTelemetry(string vehicleId, MqttApplicationMessage msg)
    {
        if (!IsValidVehicleId(vehicleId))
        {
            _logger.LogWarning("Invalid vehicleId format in telemetry: {VehicleId}", vehicleId);
            return;
        }

        using var scope = _services.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();

        var json = JsonDocument.Parse(msg.PayloadSegment);
        var root = json.RootElement;

        var capturedAt = root.TryGetProperty("capturedAt", out var capEl)
            ? DateTimeOffset.Parse(capEl.GetString()!)
            : DateTimeOffset.UtcNow;

        var loc = root.GetProperty("location");
        var lat = loc.GetProperty("latitude").GetDouble();
        var lon = loc.GetProperty("longitude").GetDouble();
        var accuracy = loc.TryGetProperty("accuracyMeters", out var accEl) ? accEl.GetDouble() : (double?)null;
        var heading = loc.TryGetProperty("heading", out var headEl) ? headEl.GetDouble() : (double?)null;
        var speed = root.TryGetProperty("speedKmh", out var spdEl) ? spdEl.GetDouble() : 0;
        var plate = root.TryGetProperty("vehiclePlate", out var pEl) ? pEl.GetString()! : "";
        var routeId = root.TryGetProperty("routeId", out var rEl) ? rEl.GetString() : null;
        var battery = root.TryGetProperty("batteryPercent", out var bEl) ? bEl.GetDouble() : 0;
        var cameraOnline = root.TryGetProperty("cameraOnline", out var cEl) && cEl.GetBoolean();
        var networkType = root.TryGetProperty("networkType", out var nEl) ? nEl.GetString()! : "Unknown";
        var signalDbm = root.TryGetProperty("signalStrengthDbm", out var sigEl) ? sigEl.GetInt32() : 0;

        var telemetryEvent = new VehicleTelemetryReceived
        {
            CorrelationId = Guid.NewGuid(),
            VehicleId = vehicleId,
            VehiclePlate = plate,
            CapturedAt = capturedAt,
            Location = new GeoLocation
            {
                Latitude = lat,
                Longitude = lon,
                AccuracyMeters = accuracy,
                Heading = heading,
            },
            SpeedKmh = speed,
            RouteId = routeId,
            BatteryPercent = battery,
            CameraOnline = cameraOnline,
            NetworkType = networkType,
            SignalStrengthDbm = signalDbm,
        };

        await publishEndpoint.PublishEvent(telemetryEvent);

        var db = redis.GetDatabase();
        var hashKey = $"vehicle:pos:{vehicleId}";
        await db.HashSetAsync(hashKey, new HashEntry[]
        {
            new("lat", lat.ToString("F6")),
            new("lon", lon.ToString("F6")),
            new("speed", speed.ToString("F1")),
            new("heading", (heading ?? 0).ToString("F1")),
            new("at", capturedAt.ToString("O")),
        });
        await db.KeyExpireAsync(hashKey, TimeSpan.FromSeconds(60));

        _logger.LogInformation("Telemetry from {VehicleId} published and cached in Redis", vehicleId);
    }

    // SKELETON: real version uses EMQX mTLS certificate + per-device topic ACLs (design record §6).
    private static bool IsValidVehicleId(string vehicleId) =>
        vehicleId.StartsWith("MUN-VEH-") && vehicleId.Length > 8;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client?.IsConnected == true)
        {
            await _client.UnsubscribeAsync(new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter("$share/vehiclecomm/vehicles/+/frames")
                .WithTopicFilter("$share/vehiclecomm/vehicles/+/telemetry")
                .Build(), cancellationToken);
            await _client.DisconnectAsync(cancellationToken: cancellationToken);
        }
        _client?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
