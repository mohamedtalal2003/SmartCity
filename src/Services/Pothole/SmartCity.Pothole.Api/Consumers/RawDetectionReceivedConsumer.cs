using MassTransit;
using NetTopologySuite.Geometries;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Detection;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.Pothole.Api.Data;
using SmartCity.Pothole.Api.Domain;

namespace SmartCity.Pothole.Api.Consumers;

public sealed class RawDetectionReceivedConsumer : IConsumer<RawDetectionReceived>
{
    private readonly PotholeDbContext _db;
    private readonly ILogger<RawDetectionReceivedConsumer> _logger;

    public RawDetectionReceivedConsumer(PotholeDbContext db, ILogger<RawDetectionReceivedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RawDetectionReceived> context)
    {
        var msg = context.Message;

        if (!msg.Type.IsRoadDamage())
            return;

        var severity = ClassifySeverity(msg.Metadata);
        var district = ClassifyDistrict(msg.Location.Latitude, msg.Location.Longitude);

        // SKELETON: real version clusters with ST_DWithin 3–5 m; this stub inserts a new row every time.
        var pothole = new PotholeRecord
        {
            Id = Guid.NewGuid(),
            SourceDetectionId = msg.DetectionId,
            CorrelationId = msg.CorrelationId,
            Location = new Point(msg.Location.Longitude, msg.Location.Latitude) { SRID = 4326 },
            District = district,
            DetectionType = msg.Type,
            Stage = DamageStage.Pothole,
            Severity = severity,
            Confidence = msg.Confidence,
            FirstSeenAt = msg.CapturedAt,
            LastSeenAt = msg.CapturedAt,
            ImageUrls = new[] { msg.ImageUrl }
        };

        _db.Potholes.Add(pothole);

        var saved = new PotholeSaved
        {
            CorrelationId = msg.CorrelationId,
            SourceDetectionId = msg.DetectionId,
            PotholeId = pothole.Id,
            IsNewPothole = true,
            DetectionCount = 1,
            FirstSeenAt = pothole.FirstSeenAt,
            LastSeenAt = pothole.LastSeenAt,
            Location = msg.Location,
            District = district,
            DetectionType = msg.Type,
            Stage = pothole.Stage,
            Severity = severity,
            Confidence = msg.Confidence,
            ImageUrls = new List<string> { msg.ImageUrl }
        };

        await context.PublishFollowUp(saved, context.CancellationToken);
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Saved pothole {PotholeId} ({Type}, {Severity}) in {District}",
            pothole.Id, msg.Type, severity, district);
    }

    // SKELETON: real version uses estimated_depth_cm thresholds from config; this uses fixed ranges.
    private static SeverityLevel ClassifySeverity(Dictionary<string, string>? metadata)
    {
        if (metadata is null || !metadata.TryGetValue("estimated_depth_cm", out var depthStr)
            || !double.TryParse(depthStr, out var depth))
            return SeverityLevel.Medium;

        return depth switch
        {
            < 4 => SeverityLevel.Low,
            < 7 => SeverityLevel.Medium,
            < 10 => SeverityLevel.High,
            _ => SeverityLevel.Critical
        };
    }

    // SKELETON: real version uses PostGIS polygons for district boundaries; this uses rough bounding boxes.
    private static District ClassifyDistrict(double lat, double lon)
    {
        if (lat < 37.88 && lon < 32.48) return District.Meram;
        if (lat >= 37.88 && lon < 32.50) return District.Selcuklu;
        return District.Karatay;
    }
}
