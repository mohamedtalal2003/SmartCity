using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

var vehicles = 5;
var intervalMs = 2000;
var frames = int.MaxValue;
var mqttHost = "localhost";
var mqttPort = 1883;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--vehicles": vehicles = int.Parse(args[++i]); break;
        case "--interval-ms": intervalMs = int.Parse(args[++i]); break;
        case "--frames": frames = int.Parse(args[++i]); break;
        case "--mqtt-host": mqttHost = args[++i]; break;
        case "--mqtt-port": mqttPort = int.Parse(args[++i]); break;
    }
}

Console.WriteLine($"Starting simulator: {vehicles} vehicles, {intervalMs}ms interval, {(frames == int.MaxValue ? "infinite" : frames)} frames");

// SKELETON: real version uses real camera frames; this is a 1x1 placeholder JPEG.
var tinyJpeg = Convert.FromBase64String(
    "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////" +
    "////////////////////////////////////////////////////////////" +
    "2wBDAf//////////////////////////////////////////////////////" +
    "////////////////////////////////////////////wAARCAABAAEDASIA" +
    "AhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAA" +
    "AAAAAAAP/aAAwDAQACEAMQAAABKp//2Q==");

var routes = new (double lat, double lon)[][]
{
    // Kadıköy loop (~40.985, 29.030)
    new[] { (40.985, 29.030), (40.986, 29.031), (40.987, 29.032), (40.988, 29.031), (40.987, 29.030), (40.986, 29.029) },
    // Beşiktaş loop (~41.043, 29.006)
    new[] { (41.043, 29.006), (41.044, 29.007), (41.045, 29.008), (41.046, 29.007), (41.045, 29.006), (41.044, 29.005) },
    // Fatih loop (~41.012, 28.955)
    new[] { (41.012, 28.955), (41.013, 28.956), (41.014, 28.957), (41.015, 28.956), (41.014, 28.955), (41.013, 28.954) },
};

var factory = new MqttFactory();
using var client = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer(mqttHost, mqttPort)
    .WithClientId($"vehicle-simulator-{Guid.NewGuid():N}"[..30])
    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
    .Build();

await client.ConnectAsync(options);
Console.WriteLine("Connected to MQTT broker");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var rng = new Random(42);
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

try
{
    for (var tick = 0; tick < frames && !cts.IsCancellationRequested; tick++)
    {
        var tasks = new List<Task>();
        for (var v = 0; v < vehicles; v++)
        {
            var vehicleId = $"MUN-VEH-{(v + 1):D3}";
            var plate = $"34 ABC {(v + 1):D3}";
            var route = routes[v % routes.Length];
            var pos = route[tick % route.Length];
            var speed = 20.0 + rng.NextDouble() * 30.0;
            var heading = rng.NextDouble() * 360.0;
            var frameId = Guid.NewGuid();
            var capturedAt = DateTimeOffset.UtcNow;

            // Frame message: payload = image bytes, metadata in MQTT v5 user properties
            var frameMsg = new MqttApplicationMessageBuilder()
                .WithTopic($"vehicles/{vehicleId}/frames")
                .WithPayload(tinyJpeg)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithUserProperty("frameId", frameId.ToString())
                .WithUserProperty("capturedAt", capturedAt.ToString("O"))
                .WithUserProperty("lat", pos.lat.ToString("F6"))
                .WithUserProperty("lon", pos.lon.ToString("F6"))
                .WithUserProperty("accuracy", "2.5")
                .WithUserProperty("heading", heading.ToString("F1"))
                .WithUserProperty("speed", speed.ToString("F1"))
                .WithUserProperty("width", "1920")
                .WithUserProperty("height", "1080")
                .WithUserProperty("contentType", "image/jpeg")
                .Build();

            // Telemetry message: JSON payload
            var telemetry = new
            {
                vehicleId,
                vehiclePlate = plate,
                capturedAt = capturedAt.ToString("O"),
                location = new { latitude = pos.lat, longitude = pos.lon, accuracyMeters = 2.5, heading },
                speedKmh = speed,
                routeId = $"ROUTE-{(v % routes.Length) + 1:D2}",
                batteryPercent = 70.0 + rng.NextDouble() * 30.0,
                cameraOnline = true,
                networkType = "4G",
                signalStrengthDbm = -70 - rng.Next(20),
            };

            var telemetryMsg = new MqttApplicationMessageBuilder()
                .WithTopic($"vehicles/{vehicleId}/telemetry")
                .WithPayload(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(telemetry, jsonOptions)))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            tasks.Add(client.PublishAsync(frameMsg, cts.Token));
            tasks.Add(client.PublishAsync(telemetryMsg, cts.Token));

            Console.WriteLine($"[{capturedAt:HH:mm:ss.fff}] {vehicleId} frameId={frameId} lat={pos.lat:F4} lon={pos.lon:F4}");
        }
        await Task.WhenAll(tasks);
        if (tick < frames - 1)
            await Task.Delay(intervalMs, cts.Token);
    }
}
catch (OperationCanceledException) { }

await client.DisconnectAsync();
Console.WriteLine("Simulator stopped");
