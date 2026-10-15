using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.Contracts.Events.Detection;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.Pothole.Application.Abstractions;
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
        if (depthCm is null)
            return SeverityLevel.Medium;

        if (depthCm < _options.LowBelowCm) return SeverityLevel.Low;
        if (depthCm < _options.MediumBelowCm) return SeverityLevel.Medium;
        if (depthCm < _options.HighBelowCm) return SeverityLevel.High;
        return SeverityLevel.Critical;
    }

    private static PotholeSaved ToPotholeSaved(Pothole pothole, AreaMatch area, RawDetectionReceived message, bool isNewPothole) => new()
    {
        // Invariant 5: copy CorrelationId unchanged and set SourceDetectionId, so the trace works.
        CorrelationId = message.CorrelationId,
        SourceDetectionId = message.DetectionId,
        PotholeId = pothole.Id,
        IsNewPothole = isNewPothole,
        Reason = isNewPothole ? PotholeSavedReason.New : PotholeSavedReason.Changed,
        DetectionCount = pothole.SightingsCount,
        RowVersion = pothole.RowVersion,
        FirstSeenAt = pothole.FirstSeenAt,
        LastSeenAt = pothole.LastSeenAt,
        Location = new GeoLocation { Latitude = pothole.Location.Latitude, Longitude = pothole.Location.Longitude },
        DistrictId = area.DistrictId,
        DistrictName = area.DistrictName,
        NeighborhoodId = area.NeighborhoodId,
        NeighborhoodName = area.NeighborhoodName,
        RoadName = pothole.RoadName,
        DetectionType = pothole.DamageType,
        Stage = pothole.CurrentStage,
        Severity = pothole.CurrentSeverity,
        EstimatedSize = pothole.AreaM2 is null && pothole.DepthCm is null
            ? null
            : new Dimensions { AreaSquareMeters = pothole.AreaM2, DepthMeters = pothole.DepthCm / 100 },
        Confidence = pothole.Confidence,
        Source = ToContractSource(pothole.Source),
        Estimate = ToEstimateInfo(pothole.Estimate),
        ImageUrls = new List<string> { message.ImageUrl }
    };

    // Pothole's own enum and the contract enum have the same names, so map them one by one.
    private static Contracts.Enums.PotholeSource ToContractSource(PotholeSource source) => source switch
    {
        PotholeSource.Ai => Contracts.Enums.PotholeSource.Ai,
        PotholeSource.Officer => Contracts.Enums.PotholeSource.Officer,
        PotholeSource.CitizenReport => Contracts.Enums.PotholeSource.CitizenReport,
        _ => Contracts.Enums.PotholeSource.Unspecified
    };

    private static CostEstimateInfo ToEstimateInfo(CostEstimate estimate) => new()
    {
        Materials = estimate.Materials,
        Labor = estimate.Labor,
        Equipment = estimate.Equipment,
        Total = estimate.Total,
        Currency = estimate.Currency,
        // SKELETON: only the money is stored on the pothole. Hours, crew, priority and material are known
        // when the estimate was computed in this call (new pothole, or severity/stage changed); a pothole
        // whose only change is its damage type sends them empty. A later PR stores or recomputes them.
        LaborHours = estimate.LaborHours,
        CrewSize = estimate.CrewSize,
        SuggestedPriority = estimate.SuggestedPriority,
        MaterialItems = estimate.Material is null
            ? new List<MaterialItem>()
            : new List<MaterialItem>
            {
                new()
                {
                    Name = estimate.Material.Name,
                    Quantity = estimate.Material.Quantity,
                    Unit = estimate.Material.Unit,
                    UnitCost = estimate.Material.Quantity > 0 ? estimate.Materials / (decimal)estimate.Material.Quantity : 0,
                    TotalCost = estimate.Materials
                }
            }
    };
}
