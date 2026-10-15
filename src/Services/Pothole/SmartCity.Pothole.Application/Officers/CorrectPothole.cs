using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Application.Abstractions;
using SmartCity.Pothole.Application.Commands;
using SmartCity.Pothole.Application.Potholes;
using SmartCity.Pothole.Domain.Estimation;

namespace SmartCity.Pothole.Application.Officers;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>An officer corrects a pothole (P2). A value left null stays as it is; the depth is in cm.</summary>
public sealed record CorrectPotholeCommand(
    CommandContext Context,
    Guid PotholeId,
    SeverityLevel? Severity,
    DamageStage? Stage,
    double? AreaM2,
    double? DepthCm,
    string Reason);

/// <summary>Use case P2: an officer corrects the AI's data. The pothole and its estimate change and
/// <c>PotholeSaved</c> goes out with Reason = Corrected.</summary>
public sealed class CorrectPothole
{
    private readonly IPotholeRepository _repository;
    private readonly IEventPublisher _publisher;
    private readonly PotholePrices _prices;
    private readonly TimeProvider _time;
    private readonly ILogger<CorrectPothole> _logger;

    public CorrectPothole(
        IPotholeRepository repository,
        IEventPublisher publisher,
        IOptions<PotholePrices> prices,
        TimeProvider time,
        ILogger<CorrectPothole> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _prices = prices.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<CommandResult> HandleAsync(CorrectPotholeCommand command, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        // 1. The same idempotency key again: return the first result.
        var earlier = await _repository.FindCommandResultAsync(command.Context.IdempotencyKey, ct);
        if (earlier is not null)
            return earlier;

        // 2. Load the pothole. A closed pothole cannot be corrected.
        var pothole = await _repository.GetByIdAsync(command.PotholeId, ct);
        if (pothole is null)
            return CommandResult.Failed(CommandError.NotFound, "Pothole not found.", now);
        if (pothole.Status == PotholeStatus.Closed)
            return CommandResult.Failed(CommandError.InvalidState, "The pothole is closed and cannot be corrected.", now);

        // 3. Correct it. No reason or nothing changed is a validation error.
        var correction = new Correction(
            command.Severity, command.Stage, command.AreaM2, command.DepthCm,
            command.Reason, command.Context.SubmittedBy, now);
        try
        {
            pothole.Correct(correction, _prices);
        }
        catch (DomainException e)
        {
            return CommandResult.Failed(CommandError.Validation, e.Message, now);
        }

        // 4. Tell the others, and save the pothole, the event and the key together.
        var area = await _repository.GetAreaAsync(pothole.DistrictId, pothole.NeighborhoodId, ct);
        await _publisher.PublishAsync(PotholeSavedMessage.Build(
            pothole,
            area,
            command.Context.CorrelationId,
            // SKELETON: a correction has no AI detection, so SourceDetectionId is the id of the Correction
            // observation (as for officer adds).
            sourceDetectionId: pothole.Observations[^1].Id,
            PotholeSavedReason.Corrected,
            imageUrls: new List<string>(), // a correction has no new photo
            _prices), ct);

        var result = CommandResult.Ok(pothole.Id, created: false, now);
        _repository.RememberCommandResult(command.Context.IdempotencyKey, result);
        await _repository.SaveChangesAsync(ct);

        _logger.LogInformation("Pothole {PotholeId} corrected by {Officer}: severity {Severity}, stage {Stage}",
            pothole.Id, command.Context.SubmittedBy, pothole.CurrentSeverity, pothole.CurrentStage);
        return result;
    }
}
