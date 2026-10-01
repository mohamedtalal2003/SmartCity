using System.Net;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace SmartCity.EndToEnd.Tests;

/// <summary>
/// Publishes one frame over MQTT with ForceDetection enabled on AiDetection,
/// then verifies the full chain: frame → pothole → cost estimate → work order.
/// Requires the local stack to be running (docker compose + all services).
/// AiDetection must have FakeAi:ForceDetection=true.
/// </summary>
public class FrameToWorkOrderTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _out;
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:5000") };
    private readonly HttpClient _s3 = new();
    private IMqttClient _mqtt = null!;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // 1x1 valid JPEG — same as the VehicleSimulator uses.
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////" +
        "////////////////////////////////////////////////////////////" +
        "2wBDAf//////////////////////////////////////////////////////" +
        "////////////////////////////////////////////wAARCAABAAEDASIA" +
        "AhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAA" +
        "AAAAAAAP/aAAwDAQACEAMQAAABKp//2Q==");

    public FrameToWorkOrderTests(ITestOutputHelper output) => _out = output;

    public async Task InitializeAsync()
    {
        var factory = new MqttFactory();
        _mqtt = factory.CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer("localhost", 1883)
            .WithClientId($"e2e-test-{Guid.NewGuid():N}"[..30])
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
            .Build();
        await _mqtt.ConnectAsync(options);
    }

    public async Task DisposeAsync()
    {
        if (_mqtt.IsConnected)
            await _mqtt.DisconnectAsync();
        _mqtt.Dispose();
        _http.Dispose();
        _s3.Dispose();
    }

    [Fact]
    public async Task PublishFrame_CreatesWorkOrderWithMatchingEstimate()
    {
        // ── 1. Publish one frame over MQTT ──────────────────────────
        var frameId = Guid.NewGuid();
        var vehicleId = "MUN-VEH-001";
        var capturedAt = DateTimeOffset.UtcNow;
        // Meram district coordinates
        const double lat = 37.851;
        const double lon = 32.432;

        var frameMsg = new MqttApplicationMessageBuilder()
            .WithTopic($"vehicles/{vehicleId}/frames")
            .WithPayload(TinyJpeg)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithUserProperty("frameId", frameId.ToString())
            .WithUserProperty("capturedAt", capturedAt.ToString("O"))
            .WithUserProperty("lat", lat.ToString("F6"))
            .WithUserProperty("lon", lon.ToString("F6"))
            .WithUserProperty("accuracy", "2.5")
            .WithUserProperty("heading", "90.0")
            .WithUserProperty("speed", "35.0")
            .WithUserProperty("width", "1920")
            .WithUserProperty("height", "1080")
            .WithUserProperty("contentType", "image/jpeg")
            .Build();

        await _mqtt.PublishAsync(frameMsg);
        _out.WriteLine($"Published frame {frameId}");

        // ── 2. Poll GET /api/work-orders until one appears with EstimatedCost > 0 ──
        // Cost and WorkOrder consumers process PotholeSaved in parallel, so the
        // estimate may be attached a few seconds after the work order is created.
        string workOrderId = null!;
        double estimatedCostFromWo = 0;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            var resp = await _http.GetAsync("/api/work-orders");
            resp.EnsureSuccessStatusCode();
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var records = doc.RootElement.GetProperty("records");
            if (records.GetArrayLength() > 0)
            {
                var wo = records[0];
                var cost = wo.GetProperty("estimatedCost").GetDouble();
                workOrderId = wo.GetProperty("workOrderId").GetString()!;
                if (cost > 0)
                {
                    estimatedCostFromWo = cost;
                    break;
                }
                _out.WriteLine($"Work order {workOrderId} found, waiting for cost estimate...");
            }
        }
        Assert.NotNull(workOrderId);
        Assert.True(estimatedCostFromWo > 0, "Work order should have an estimated cost within 30 s");
        _out.WriteLine($"Work order: {workOrderId}, estimated cost: {estimatedCostFromWo}");

        // ── 3. Find the pothole by image URL containing our frameId ──
        var potholesResp = await _http.GetAsync("/api/potholes");
        potholesResp.EnsureSuccessStatusCode();
        var potholesDoc = JsonDocument.Parse(await potholesResp.Content.ReadAsStringAsync());
        var features = potholesDoc.RootElement.GetProperty("features");

        string? potholeId = null;
        string? imageUrl = null;
        foreach (var feature in features.EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            var url = props.GetProperty("latestImageUrl").GetString() ?? "";
            if (url.Contains(frameId.ToString()))
            {
                potholeId = props.GetProperty("damageId").GetString();
                imageUrl = url;
                break;
            }
        }
        Assert.NotNull(potholeId);
        Assert.NotNull(imageUrl);
        _out.WriteLine($"Pothole found: {potholeId}, image: {imageUrl}");

        // ── 4. Verify the pothole detail endpoint ──
        var potholeDetailResp = await _http.GetAsync($"/api/potholes/{potholeId}");
        Assert.Equal(HttpStatusCode.OK, potholeDetailResp.StatusCode);
        var potholeDetail = JsonDocument.Parse(await potholeDetailResp.Content.ReadAsStringAsync());
        var detailImageUrl = potholeDetail.RootElement.GetProperty("latestImageUrl").GetString();
        Assert.Contains(frameId.ToString(), detailImageUrl);
        _out.WriteLine("Pothole detail verified");

        // ── 5. HTTP GET the image from blob storage → 200 ──
        var imageResp = await _s3.GetAsync(imageUrl);
        Assert.Equal(HttpStatusCode.OK, imageResp.StatusCode);
        var imageBytes = await imageResp.Content.ReadAsByteArrayAsync();
        Assert.True(imageBytes.Length > 0, "Image should have content");
        _out.WriteLine($"Image fetched OK, {imageBytes.Length} bytes");

        // ── 6. Verify estimate matches work order's EstimatedCost ──
        var estimateResp = await _http.GetAsync($"/api/estimates/by-entity/{potholeId}");
        Assert.Equal(HttpStatusCode.OK, estimateResp.StatusCode);
        var estimate = JsonDocument.Parse(await estimateResp.Content.ReadAsStringAsync());
        var totalCostFromEstimate = estimate.RootElement.GetProperty("totalCost").GetDouble();
        _out.WriteLine($"Estimate total cost: {totalCostFromEstimate}");

        Assert.Equal(totalCostFromEstimate, estimatedCostFromWo, precision: 2);
        _out.WriteLine("Estimate matches work order's EstimatedCost ✓");
    }
}
