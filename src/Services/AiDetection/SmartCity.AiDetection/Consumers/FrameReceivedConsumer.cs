using MassTransit;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Ingestion;

namespace SmartCity.AiDetection.Consumers;

// SKELETON: real version runs a YOLO model on the downloaded image; this stub
// rolls a dice and emits fake detections with random bounding boxes.
public sealed class FrameReceivedConsumer : IConsumer<FrameReceived>
{
    private readonly ILogger<FrameReceivedConsumer> _logger;
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;
    private static readonly ThreadLocal<Random> Rng = new(() => new Random());

    private static readonly DetectionType[] RoadDamageTypes = [DetectionType.Pothole, DetectionType.RoadCrack, DetectionType.SurfaceDamage];
    private static readonly DetectionType[] AssetTypes = [DetectionType.TrafficLight, DetectionType.TrafficSign, DetectionType.StreetLight, DetectionType.Barrier, DetectionType.StreetFurniture];
    private static readonly DetectionType[] ViolationTypes = [DetectionType.CommercialSign, DetectionType.Banner, DetectionType.Billboard];
    private static readonly DetectionType[] BuildingTypes = [DetectionType.Scaffolding, DetectionType.ConstructionSite, DetectionType.BuildingFacade];

    public FrameReceivedConsumer(ILogger<FrameReceivedConsumer> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _config = config;
        _httpClient = httpClientFactory.CreateClient("S3Download");
    }

    public async Task Consume(ConsumeContext<FrameReceived> context)
    {
        var frame = context.Message;
        _logger.LogInformation("Processing frame {FrameId} from {VehicleId}", frame.FrameId, frame.VehicleId);

        var response = await _httpClient.GetAsync(frame.ImageUrl, context.CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to download image at {ImageUrl}: HTTP {StatusCode}", frame.ImageUrl, (int)response.StatusCode);
            return;
        }

        var imageBytes = await response.Content.ReadAsByteArrayAsync(context.CancellationToken);
        _logger.LogInformation("Downloaded {Bytes} bytes from {ImageUrl}", imageBytes.Length, frame.ImageUrl);

        var rng = Rng.Value!;
        var detectionRate = _config.GetValue("FakeAi:DetectionRate", 0.02);
        var forceDetection = _config.GetValue("FakeAi:ForceDetection", false);
        var emitAllTypes = _config.GetValue("FakeAi:EmitAllTypes", false);

        if (!forceDetection && rng.NextDouble() > detectionRate)
        {
            _logger.LogInformation("No detection for frame {FrameId}", frame.FrameId);
            return;
        }

        if (emitAllTypes)
        {
            await EmitDetection(context, frame, PickRandom(rng, RoadDamageTypes), rng);

            if (rng.NextDouble() < 0.3)
                await EmitDetection(context, frame, PickRandom(rng, AssetTypes), rng);
            if (rng.NextDouble() < 0.3)
                await EmitDetection(context, frame, PickRandom(rng, ViolationTypes), rng);
            if (rng.NextDouble() < 0.3)
                await EmitDetection(context, frame, PickRandom(rng, BuildingTypes), rng);
        }
        else
        {
            var type = rng.NextDouble() < 0.8
                ? PickRandom(rng, RoadDamageTypes)
                : PickRandom(rng, [.. AssetTypes, .. ViolationTypes, .. BuildingTypes]);

            await EmitDetection(context, frame, type, rng);
        }
    }

    private async Task EmitDetection(ConsumeContext<FrameReceived> context, FrameReceived frame, DetectionType type, Random rng)
    {
        var detectionId = Guid.NewGuid();
        var metadata = new Dictionary<string, string>();

        if (type.IsRoadDamage())
            metadata["estimated_depth_cm"] = rng.Next(2, 13).ToString();

        var detection = new RawDetectionReceived
        {
            DetectionId = detectionId,
            CorrelationId = frame.CorrelationId,
            FrameId = frame.FrameId,
            VehicleId = frame.VehicleId,
            CapturedAt = frame.CapturedAt,
            Location = frame.Location,
            SpeedKmh = frame.SpeedKmh,
            Type = type,
            Confidence = Math.Round(0.6 + rng.NextDouble() * 0.38, 2),
            ModelName = "fake-ai",
            ModelVersion = "0.1.0-skeleton",
            BboxX = Math.Round(rng.NextDouble() * 0.5, 3),
            BboxY = Math.Round(rng.NextDouble() * 0.5, 3),
            BboxWidth = Math.Round(0.05 + rng.NextDouble() * 0.3, 3),
            BboxHeight = Math.Round(0.05 + rng.NextDouble() * 0.3, 3),
            ImageUrl = frame.ImageUrl,
            Metadata = metadata,
        };

        await context.PublishFollowUp(detection);
        _logger.LogInformation("Detection {DetectionId} type={Type} confidence={Confidence} for frame {FrameId}",
            detectionId, type, detection.Confidence, frame.FrameId);
    }

    private static T PickRandom<T>(Random rng, T[] items) => items[rng.Next(items.Length)];
}
