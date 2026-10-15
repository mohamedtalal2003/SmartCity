using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Detection;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.Pothole.Application.Abstractions;
using SmartCity.Pothole.Application.Potholes;
using SmartCity.Pothole.Domain.Estimation;

namespace SmartCity.Pothole.Application.Detections;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Use case: one road-damage detection from the AI comes in. It becomes a sighting of an open
/// pothole nearby, a sighting of a recent false positive, or a new pothole.
/// </summary>
public sealed class HandleDetection
{
    private readonly IPotholeRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly DetectionOptions _options;
    private readonly PotholePrices _prices;
    private readonly TimeProvider _time;
    private readonly ILogger<HandleDetection> _logger;

    public HandleDetection(
        IPotholeRepository repository,
        IEventPublisher publisher,
        IOptions<DetectionOptions> options,
        IOptions<PotholePrices> prices,
        TimeProvider time,
        ILogger<HandleDetection> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _options = options.Value;
        _prices = prices.Value;
        _time = time;
        _logger = logger;
    }

    public async Task HandleAsync(RawDetectionReceived message, CancellationToken ct)
    {
        // 1. The same detection was already saved: do nothing (RabbitMQ delivers at least once).
        if (await _repository.ObservationExistsAsync(message.DetectionId, ct))
        {
            _logger.LogInformation("Detection {DetectionId} was already saved, skipping", message.DetectionId);
            return;
        }

        var detection = ToDetection(message);
        var radius = _options.MatchRadiusMeters;

        // 2. An open pothole nearby: this is another sighting of it. Any road-damage type matches any
        //    other (a crack and a pothole are one damage). Low confidence is fine here.
        var open = await _repository.FindNearestOpenAsync(detection.Location, radius, ct);
        if (open is not null)
        {
            var changed = open.AddSighting(detection, _prices);
            if (changed)
            {
                var openArea = await _repository.GetAreaAsync(open.DistrictId, open.NeighborhoodId, ct);
                await _publisher.PublishAsync(ToPotholeSaved(open, openArea, message, isNewPothole: false), ct);
            }

            await _repository.SaveChangesAsync(ct);
            _logger.LogInformation("Detection {DetectionId} added to pothole {PotholeId} (sighting {Count}, changed: {Changed})",
                message.DetectionId, open.Id, open.SightingsCount, changed);
            return;
        }

        // 3. Too unsure to create a new pothole.
        if (detection.Confidence < _options.MinConfidenceForNew)
        {
            _logger.LogInformation("Detection {DetectionId} ignored: confidence {Confidence} is below {Min}",
                message.DetectionId, detection.Confidence, _options.MinConfidenceForNew);
            return;
        }

        // 4. A pothole here was closed as a mistake not long ago: only keep the observation on it.
        var since = _time.GetUtcNow().AddDays(-_options.FalsePositiveSuppressionDays);
        var falsePositive = await _repository.FindRecentFalsePositiveAsync(detection.Location, radius, since, ct);
        if (falsePositive is not null)
        {
            falsePositive.AddSuppressedSighting(detection);
            await _repository.SaveChangesAsync(ct);
            _logger.LogInformation("Detection {DetectionId} saved on pothole {PotholeId}, which was closed as FalsePositive",
                message.DetectionId, falsePositive.Id);
            return;
        }

        // 5. A new pothole.
        var area = await _repository.FindAreaAsync(detection.Location, ct);
        var pothole = Pothole.FromDetection(detection, area.DistrictId, area.NeighborhoodId, _prices);
        _repository.Add(pothole);
        await _publisher.PublishAsync(ToPotholeSaved(pothole, area, message, isNewPothole: true), ct);

        // 6. One save: the outbox commits the row and the event together.
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("New pothole {PotholeId} ({Type}, {Severity}) in district {DistrictId}, neighborhood {NeighborhoodId}",
            pothole.Id, pothole.DamageType, pothole.CurrentSeverity, pothole.DistrictId, pothole.NeighborhoodId);
    }

    private Detection ToDetection(RawDetectionReceived message)
    {
        var depthCm = ReadDepthCm(message.Metadata);

        return new Detection(
            DetectionId: message.DetectionId,
            Location: new GeoPoint(message.Location.Latitude, message.Location.Longitude),
            DamageType: message.Type,
            Severity: SeverityFromDepth(depthCm),
            // SKELETON: the AI does not send a stage yet, so it is guessed from the type. The real AI sends the stage.
            Stage: message.Type == DetectionType.Pothole ? DamageStage.Pothole : DamageStage.Unspecified,
            // SKELETON: the AI does not measure the area yet. It stays empty until the AI sends it.
            AreaM2: null,
            DepthCm: depthCm,
            Confidence: message.Confidence,
            ImageUrl: message.ImageUrl,
            SeenAt: message.CapturedAt);
    }

    private static double? ReadDepthCm(Dictionary<string, string>? metadata)
    {
        if (metadata is not null
            && metadata.TryGetValue("estimated_depth_cm", out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth))
            return depth;

        return null;
    }

    private SeverityLevel SeverityFromDepth(double? depthCm)
    {
        // SKELETON: with no depth from the AI the severity is Medium (as in the old stub). The real AI
        // will always send its measurements.
        return depthCm is null ? SeverityLevel.Medium : _options.SeverityFromDepth(depthCm.Value);
    }

    private PotholeSaved ToPotholeSaved(Pothole pothole, AreaMatch area, RawDetectionReceived message, bool isNewPothole) =>
        PotholeSavedMessage.Build(
            pothole,
            area,
            // Invariant 5: copy CorrelationId unchanged and set SourceDetectionId, so the trace works.
            correlationId: message.CorrelationId,
            sourceDetectionId: message.DetectionId,
            reason: isNewPothole ? PotholeSavedReason.New : PotholeSavedReason.Changed,
            imageUrls: new List<string> { message.ImageUrl },
            _prices);
}
