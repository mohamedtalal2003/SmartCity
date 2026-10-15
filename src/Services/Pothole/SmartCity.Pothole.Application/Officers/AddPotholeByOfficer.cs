using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Application.Abstractions;
using SmartCity.Pothole.Application.Commands;
using SmartCity.Pothole.Application.Detections;
using SmartCity.Pothole.Application.Potholes;
using SmartCity.Pothole.Domain.Estimation;

namespace SmartCity.Pothole.Application.Officers;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>An officer adds a pothole (P1). Area and depth are null when not given; the depth is in cm.</summary>
public sealed record AddPotholeCommand(
    CommandContext Context,
    double Latitude,
    double Longitude,
    string ImageUrl,
    SeverityLevel? Severity,
    double? AreaM2,
    double? DepthCm,
    PotholeSource Source,
    string? Notes);

/// <summary>
/// Use case P1: an officer adds a pothole with a photo. Next to an open pothole (3 m) the report is added
/// to it; otherwise a new pothole is made. The AI rules (confidence, 30-day FalsePositive) do not apply.
/// </summary>
public sealed class AddPotholeByOfficer
{
    private readonly IPotholeRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly DetectionOptions _detection;
    private readonly BlobStorageOptions _blobStorage;
    private readonly PotholePrices _prices;
    private readonly TimeProvider _time;
    private readonly ILogger<AddPotholeByOfficer> _logger;

    public AddPotholeByOfficer(
        IPotholeRepository repository,
        IEventPublisher publisher,
        IOptions<DetectionOptions> detection,
        IOptions<BlobStorageOptions> blobStorage,
        IOptions<PotholePrices> prices,
        TimeProvider time,
        ILogger<AddPotholeByOfficer> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _detection = detection.Value;
        _blobStorage = blobStorage.Value;
        _prices = prices.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<CommandResult> HandleAsync(AddPotholeCommand command, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        // 1. The same idempotency key again: return the first result.
        var earlier = await _repository.FindCommandResultAsync(command.Context.IdempotencyKey, ct);
        if (earlier is not null)
            return earlier;

        // 2. Check the input.
        if (!_blobStorage.IsOurUrl(command.ImageUrl))
            return CommandResult.Failed(CommandError.Validation, "The image must be uploaded to our blob storage first.", now);
        if (command.Severity is null && command.AreaM2 is null && command.DepthCm is null)
            return CommandResult.Failed(CommandError.Validation, "Give the severity or the size (area or depth).", now);
        if (command.Source == PotholeSource.Ai)
            return CommandResult.Failed(CommandError.Validation, "The source must be Officer or CitizenReport.", now);

        // 3. The district and neighborhood. No district means the place is outside Istanbul.
        var location = new GeoPoint(command.Latitude, command.Longitude);
        var area = await _repository.FindAreaAsync(location, ct);
        if (area.DistrictId is null)
            return CommandResult.Failed(CommandError.Validation, "The place is outside Istanbul.", now);

        var report = new OfficerReport(
            location, command.ImageUrl, SeverityFor(command), command.AreaM2, command.DepthCm,
            command.Source, command.Notes, command.Context.SubmittedBy, now);

        try
        {
            // 4. An open pothole within 3 m: the report is one more observation of it. Nothing important
            //    changed, so no event.
            var open = await _repository.FindNearestOpenAsync(location, _detection.MatchRadiusMeters, ct);
            if (open is not null)
            {
                open.AddReport(report);
                var added = CommandResult.Ok(open.Id, created: false, now);
                await SaveAsync(command.Context, added, ct);
                _logger.LogInformation("Officer report added to pothole {PotholeId}", open.Id);
                return added;
            }

            // 5. A new pothole.
            var pothole = Pothole.AddedByOfficer(report, area.DistrictId, area.NeighborhoodId, _prices);
            _repository.Add(pothole);
            await _publisher.PublishAsync(PotholeSavedMessage.Build(
                pothole,
                area,
                command.Context.CorrelationId,
                // SKELETON: an officer command has no AI detection, so SourceDetectionId is the id of the
                // OfficerAdd observation. The design does not say what it should be for officer rows.
                sourceDetectionId: pothole.Observations[0].Id,
                PotholeSavedReason.New,
                imageUrls: new List<string> { command.ImageUrl },
                _prices), ct);

            var created = CommandResult.Ok(pothole.Id, created: true, now);
            await SaveAsync(command.Context, created, ct);
            _logger.LogInformation("Officer added pothole {PotholeId} ({Severity}) in district {DistrictId}",
                pothole.Id, pothole.CurrentSeverity, pothole.DistrictId);
            return created;
        }
        catch (DomainException e)
        {
            return CommandResult.Failed(CommandError.Validation, e.Message, now);
        }
    }

    private SeverityLevel SeverityFor(AddPotholeCommand command)
    {
        if (command.Severity is not null)
            return command.Severity.Value;
        if (command.DepthCm is not null)
            return _detection.SeverityFromDepth(command.DepthCm.Value);

        // SKELETON: only the area was given. There is no area-to-severity rule yet, so it is Medium.
        return SeverityLevel.Medium;
    }

    // One save: the pothole, the event (outbox) and the idempotency key commit together.
    private async Task SaveAsync(CommandContext context, CommandResult result, CancellationToken ct)
    {
        _repository.RememberCommandResult(context.IdempotencyKey, result);
        await _repository.SaveChangesAsync(ct);
    }
}
