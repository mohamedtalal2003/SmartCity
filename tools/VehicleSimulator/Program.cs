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

// Tiny valid JPEG (a 1x1 red pixel).
var tinyJpeg = Convert.FromBase64String(
    "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////" +
    "////////////////////////////////////////////////////////////" +
    "2wBDAf//////////////////////////////////////////////////////" +
    "////////////////////////////////////////////wAARCAABAAEDASIA" +
    "AhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAA" +
    "AAAAAAAP/aAAwDAQACEAMQAAABKp//2Q==");

var routes = new (double lat, double lon)[][]
{
    // Meram loop (~37.85, 32.43)
    new[] { (37.850, 32.430), (37.851, 32.432), (37.853, 32.434), (37.855, 32.433), (37.854, 32.431), (37.852, 32.429) },
    // Selcuklu loop (~37.94, 32.50)
    new[] { (37.940, 32.500), (37.941, 32.502), (37.943, 32.504), (37.944, 32.503), (37.942, 32.501), (37.940, 32.499) },
    // Karatay loop (~37.87, 32.52)
    new[] { (37.870, 32.520), (37.871, 32.522), (37.873, 32.524), (37.874, 32.523), (37.872, 32.521), (37.870, 32.519) },
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
            var plate = $"42 ABC {(v + 1):D3}";
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
