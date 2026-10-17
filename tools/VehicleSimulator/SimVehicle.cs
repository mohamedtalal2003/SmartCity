using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Smartcity.Vehicle.V1;

/// <summary>
/// One simulated vehicle that follows the Pi rules in docs/design/vehicle/vehicle-protocol.md:
/// <list type="bullet">
/// <item>MQTT: connects with a Last Will (offline), publishes retained online, telemetry every 2 s, and listens to
/// fleet/status and its config.</item>
/// <item>Frames: every photo goes to the "card" (an in-memory queue) first, then is uploaded with gRPC, oldest first,
/// at most maxUploadsPerSecond. A frame leaves the card only on SAVED / DUPLICATE / REJECTED (or when older than
/// frameTtlHours). On UNAVAILABLE, RESOURCE_EXHAUSTED or a network error it stays and is retried with backoff;
/// on UNAUTHENTICATED / PERMISSION_DENIED uploading stops and the oldest frame is tried again later.</item>
/// </list>
/// </summary>
public sealed class SimVehicle
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan TelemetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly (double Lat, double Lon) Ankara = (39.93, 32.85);

    // Three small loops in Istanbul.
    private static readonly (double Lat, double Lon)[][] Routes =
    {
        // Kadıköy loop (~40.985, 29.030)
        new[] { (40.985, 29.030), (40.986, 29.031), (40.987, 29.032), (40.988, 29.031), (40.987, 29.030), (40.986, 29.029) },
        // Beşiktaş loop (~41.043, 29.006)
        new[] { (41.043, 29.006), (41.044, 29.007), (41.045, 29.008), (41.046, 29.007), (41.045, 29.006), (41.044, 29.005) },
        // Fatih loop (~41.012, 28.955)
        new[] { (41.012, 28.955), (41.013, 28.956), (41.014, 28.957), (41.015, 28.956), (41.014, 28.955), (41.013, 28.954) },
    };

    // SKELETON: real version uses real camera frames; this is a 1x1 placeholder JPEG.
    private static readonly ByteString TinyJpeg = ByteString.FromBase64(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////" +
        "////////////////////////////////////////////////////////////" +
        "2wBDAf//////////////////////////////////////////////////////" +
        "////////////////////////////////////////////wAARCAABAAEDASIA" +
        "AhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAA" +
        "AAAAAAAP/aAAwDAQACEAMQAAABKp//2Q==");

    private readonly SimOptions _options;
    private readonly Stats _stats;
    private readonly (double Lat, double Lon)[] _route;
    private readonly string _routeId;
    private readonly Random _rng;

    private readonly Queue<UploadFrameRequest> _card = new();   // the SD card, oldest first
    private readonly object _cardLock = new();
    private (double Lat, double Lon) _position;
    private volatile bool _captureDone;

    private readonly IMqttClient _mqtt = new MqttFactory().CreateMqttClient();
    private VehicleIngestService.VehicleIngestServiceClient _ingest = null!;

    // Set by the server over MQTT.
    private volatile bool _paused;
    private volatile int _maxUploadsPerSecond = 2;
    private volatile int _frameTtlHours = 24;

    public SimVehicle(int number, SimOptions options, Stats stats)
    {
        Id = $"MUN-VEH-{number:D3}";
        _options = options;
        _stats = stats;
        _route = Routes[(number - 1) % Routes.Length];
        _routeId = $"ROUTE-{(number - 1) % Routes.Length + 1:D2}";
        _position = _route[0];
        _rng = new Random(number);
    }

    public string Id { get; }
    public bool Paused => _paused;

    public int Pending
    {
        get { lock (_cardLock) return _card.Count; }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var channel = GrpcChannel.ForAddress(_options.GrpcUrl);   // one HTTP/2 connection for all frames
        _ingest = new VehicleIngestService.VehicleIngestServiceClient(channel);
        _mqtt.ApplicationMessageReceivedAsync += OnServerMessageAsync;

        // MQTT and telemetry run until the photos are taken and the card is empty (or Ctrl+C).
        using var background = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var mqttAndTelemetry = Task.WhenAll(KeepMqttConnectedAsync(background.Token), TelemetryLoopAsync(background.Token));

        await Task.WhenAll(CaptureLoopAsync(ct), UploadLoopAsync(ct));

        background.Cancel();
        await mqttAndTelemetry;
        await GoOfflineAsync();
    }

    // ─── MQTT ─────────────────────────────────────────────────────

    private async Task KeepMqttConnectedAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.MqttHost, _options.MqttPort)
            .WithClientId(Id)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
            // Last Will: EMQX publishes this by itself if the vehicle disappears.
            .WithWillTopic($"vehicles/{Id}/status")
            .WithWillPayload("{\"state\":\"offline\"}")
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        while (!ct.IsCancellationRequested)
        {
            if (!_mqtt.IsConnected)
            {
                try
                {
                    await _mqtt.ConnectAsync(options, ct);
                    await PublishAsync($"vehicles/{Id}/status", "{\"state\":\"online\"}", retained: true, ct);
                    await _mqtt.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter("fleet/status", MqttQualityOfServiceLevel.AtLeastOnce)
                        .WithTopicFilter($"vehicles/{Id}/config", MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build(), ct);
                    Log("MQTT connected, online");
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    Log($"MQTT connect failed: {e.Message}");
                }
            }
            if (!await SleepAsync(TimeSpan.FromSeconds(3), ct))
                break;
        }
    }

    private Task OnServerMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            using var json = JsonDocument.Parse(e.ApplicationMessage.PayloadSegment);
            var root = json.RootElement;

            if (e.ApplicationMessage.Topic == "fleet/status")
            {
                var paused = root.GetProperty("state").GetString() == "paused";
                if (paused != _paused)
                    Log(paused ? $"fleet/status: paused ({root.GetProperty("reason").GetString()}), uploads stop" : "fleet/status: running, uploads go on");
                _paused = paused;
            }
            else
            {
                _maxUploadsPerSecond = Math.Max(1, root.GetProperty("maxUploadsPerSecond").GetInt32());
                _frameTtlHours = root.GetProperty("frameTtlHours").GetInt32();
            }
        }
        catch (Exception ex)
        {
            Log($"Bad message on {e.ApplicationMessage.Topic}: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private async Task TelemetryLoopAsync(CancellationToken ct)
    {
        while (await SleepAsync(TelemetryInterval, ct))
        {
            if (!_mqtt.IsConnected)
                continue;

            var telemetry = new
            {
                capturedAt = DateTimeOffset.UtcNow.ToString("O"),
                location = new { latitude = _position.Lat, longitude = _position.Lon, accuracyMeters = 2.5, heading = _rng.NextDouble() * 360.0 },
                speedKmh = 20.0 + _rng.NextDouble() * 30.0,
                moving = true,
                pendingFrames = Pending,
                freeStorageMb = 18000,
                signalStrengthDbm = -70 - _rng.Next(20),
                networkType = "4G",
                batteryPercent = 70.0 + _rng.NextDouble() * 30.0,
                cameraOnline = true,
                routeId = _routeId,
                softwareVersion = "simulator-1.0",
            };
            try
            {
                await PublishAsync($"vehicles/{Id}/telemetry", JsonSerializer.Serialize(telemetry, Json), retained: false, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                Log($"Telemetry not sent: {e.Message}");
            }
        }
    }

    // A clean stop: say offline ourselves (a clean MQTT disconnect does not send the Last Will).
    private async Task GoOfflineAsync()
    {
        if (!_mqtt.IsConnected)
            return;
        await PublishAsync($"vehicles/{Id}/status", "{\"state\":\"offline\"}", retained: true, CancellationToken.None);
        await _mqtt.DisconnectAsync();
    }

    private Task PublishAsync(string topic, string json, bool retained, CancellationToken ct) =>
        _mqtt.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retained)
            .Build(), ct);

    // ─── Frames ───────────────────────────────────────────────────

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        for (var n = 1; n <= _options.Frames && !ct.IsCancellationRequested; n++)
        {
            _position = _route[n % _route.Length];
            var at = _options.OutsideIstanbulEvery > 0 && n % _options.OutsideIstanbulEvery == 0 ? Ankara : _position;

            var frame = new UploadFrameRequest
            {
                FrameId = Guid.NewGuid().ToString(),
                CapturedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Latitude = at.Lat,
                Longitude = at.Lon,
                GpsAccuracyM = 2.5,
                GpsSatellites = 9,
                HeadingDeg = _rng.NextDouble() * 360.0,
                SpeedKmh = 20.0 + _rng.NextDouble() * 30.0,
                Image = TinyJpeg,
                ContentType = "image/jpeg",
                WidthPx = 1920,
                HeightPx = 1080,
            };

            // Card first, then upload (rule 1). A resend puts the same frame (same id) on the card twice.
            lock (_cardLock)
            {
                _card.Enqueue(frame);
                if (_options.ResendEvery > 0 && n % _options.ResendEvery == 0)
                    _card.Enqueue(frame);
            }

            if (n < _options.Frames && !await SleepAsync(TimeSpan.FromMilliseconds(_options.IntervalMs), ct))
                break;
        }
        _captureDone = true;
    }

    private async Task UploadLoopAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.Zero;
        while (!ct.IsCancellationRequested && !(_captureDone && Pending == 0))
        {
            if (_paused)
            {
                await SleepAsync(TimeSpan.FromMilliseconds(500), ct);   // rule 6: keep capturing, do not upload
                continue;
            }

            DropExpiredFrames();
            UploadFrameRequest? frame;
            lock (_cardLock)
                frame = _card.Count > 0 ? _card.Peek() : null;
            if (frame is null)
            {
                await SleepAsync(TimeSpan.FromMilliseconds(200), ct);
                continue;
            }

            switch (await UploadAsync(frame, ct))
            {
                case Outcome.Done:
                    lock (_cardLock)
                    {
                        if (_card.Count > 0 && ReferenceEquals(_card.Peek(), frame))
                            _card.Dequeue();
                    }
                    backoff = TimeSpan.Zero;
                    await SleepAsync(TimeSpan.FromMilliseconds(1000.0 / _maxUploadsPerSecond), ct);   // rule 4
                    break;

                case Outcome.Retry:
                    backoff = backoff == TimeSpan.Zero ? TimeSpan.FromSeconds(1) : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                    await SleepAsync(backoff, ct);
                    break;

                case Outcome.Blocked:
                    await SleepAsync(TimeSpan.FromSeconds(_options.BlockedRetrySeconds), ct);   // rule 7
                    break;
            }
        }
    }

    private enum Outcome { Done, Retry, Blocked }

    private async Task<Outcome> UploadAsync(UploadFrameRequest frame, CancellationToken ct)
    {
        string reply;
        Outcome outcome;
        try
        {
            var response = await _ingest.UploadFrameAsync(frame,
                new Metadata { { "x-vehicle-id", Id } },   // dev only; the real Pi is identified by its certificate
                deadline: DateTime.UtcNow.AddSeconds(30),
                cancellationToken: ct);

            reply = response.Result == UploadResult.Rejected
                ? $"REJECTED:{response.RejectReason}"
                : response.Result.ToString().ToUpperInvariant();
            outcome = Outcome.Done;
        }
        catch (RpcException e) when (ct.IsCancellationRequested && e.StatusCode == StatusCode.Cancelled)
        {
            return Outcome.Retry;
        }
        catch (RpcException e)
        {
            (reply, outcome) = e.StatusCode switch
            {
                StatusCode.Unauthenticated => ("UNAUTHENTICATED", Outcome.Blocked),
                StatusCode.PermissionDenied => ("PERMISSION_DENIED", Outcome.Blocked),
                StatusCode.ResourceExhausted => ("RESOURCE_EXHAUSTED", Outcome.Retry),
                StatusCode.Unavailable => ("UNAVAILABLE", Outcome.Retry),     // storage down, or the server cannot be reached
                _ => ("NETWORK", Outcome.Retry),   // deadline exceeded or anything else: no real answer
            };
        }

        _stats.Count(reply);
        Log($"frame {frame.FrameId[..8]} -> {reply} (card: {Pending})");
        return outcome;
    }

    // Rule 5: frames older than frameTtlHours are deleted without being sent.
    private void DropExpiredFrames()
    {
        var oldest = DateTimeOffset.UtcNow.AddHours(-_frameTtlHours).ToUnixTimeMilliseconds();
        lock (_cardLock)
        {
            while (_card.Count > 0 && _card.Peek().CapturedAtUtc < oldest)
            {
                _card.Dequeue();
                _stats.Count("EXPIRED");
            }
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────

    /// <summary>False when cancelled.</summary>
    private static async Task<bool> SleepAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {Id} {message}");
}
