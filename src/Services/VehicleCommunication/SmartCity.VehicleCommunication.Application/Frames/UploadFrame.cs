using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Ingestion;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Domain.Frames;

namespace SmartCity.VehicleCommunication.Application.Frames;

/// <summary>One frame from a vehicle: the image and its data. <see cref="VehicleId"/> comes from the caller's identity.</summary>
public sealed record UploadFrameCommand(
    string VehicleId,
    string FrameId,
    DateTimeOffset CapturedAt,
    double Latitude,
    double Longitude,
    double GpsAccuracyMeters,
    int GpsSatellites,
    double HeadingDeg,
    double SpeedKmh,
    byte[] Image,
    string ContentType,
    int WidthPx,
    int HeightPx);

/// <summary>What happened to an uploaded frame. The gRPC adapter turns it into a reply or a status code.</summary>
public enum UploadStatus
{
    Saved,              // reply SAVED
    Duplicate,          // reply DUPLICATE
    Rejected,           // reply REJECTED + reason
    UnknownVehicle,     // UNAUTHENTICATED
    NotAllowed,         // PERMISSION_DENIED (Suspended or Retired)
    TooFast,            // RESOURCE_EXHAUSTED
    Unavailable         // UNAVAILABLE (storage down)
}

public sealed record UploadOutcome(UploadStatus Status, RejectReason? RejectReason = null)
{
    public static UploadOutcome Of(UploadStatus status) => new(status);
    public static UploadOutcome Rejected(RejectReason reason) => new(UploadStatus.Rejected, reason);
}

/// <summary>
/// Use case V8: a vehicle uploads a frame. The reply SAVED is the proof the frame is saved; the vehicle
/// deletes it from its card only after that.
///
/// The order matters. If the server dies after the image is saved but before the row is written, the vehicle
/// sends the frame again: the image is saved again under the same key, nothing is doubled. If it dies after
/// the row is written but before the reply arrives, the resend finds the row and gets DUPLICATE.
/// </summary>
public sealed class UploadFrame
{
    private readonly IVehicleRepository _repository;
    private readonly IFrameStorage _storage;
    private readonly IEventPublisher _publisher;
    private readonly FrameRateLimiter _rateLimiter;
    private readonly FrameCheckOptions _checks;
    private readonly TimeProvider _time;
    private readonly ILogger<UploadFrame> _logger;

    public UploadFrame(
        IVehicleRepository repository,
        IFrameStorage storage,
        IEventPublisher publisher,
        FrameRateLimiter rateLimiter,
        IOptions<FrameCheckOptions> checks,
        TimeProvider time,
        ILogger<UploadFrame> logger)
    {
        _repository = repository;
        _storage = storage;
        _publisher = publisher;
        _rateLimiter = rateLimiter;
        _checks = checks.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<UploadOutcome> HandleAsync(UploadFrameCommand command, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["VehicleId"] = command.VehicleId,
            ["CorrelationId"] = command.FrameId     // the frame id starts the trace (FrameReceived.CorrelationId)
        });
        var now = _time.GetUtcNow();

        // 1. Who is sending (V11).
        var vehicle = await _repository.GetAsync(command.VehicleId, ct);
        if (vehicle is null)
        {
            _logger.LogWarning("Frame from unknown vehicle {VehicleId}", command.VehicleId);
            return UploadOutcome.Of(UploadStatus.UnknownVehicle);
        }
        if (vehicle.Status != VehicleStatus.Active)
        {
            _logger.LogWarning("Frame from vehicle {VehicleId} refused: it is {Status}", command.VehicleId, vehicle.Status);
            return UploadOutcome.Of(UploadStatus.NotAllowed);
        }

        // 2. Not too fast (V12).
        if (!_rateLimiter.TryAcquire(command.VehicleId))
            return UploadOutcome.Of(UploadStatus.TooFast);

        // 3. The checks (V10). Every reject is logged with the vehicle id, to spot a vehicle with a problem.
        var reason = FrameCheck.Check(
            new FrameData(command.FrameId, command.CapturedAt, command.Latitude, command.Longitude,
                command.GpsAccuracyMeters, command.Image.Length, command.ContentType),
            now, _checks);
        if (reason is not null)
        {
            _logger.LogWarning("Frame {FrameId} from vehicle {VehicleId} rejected: {RejectReason}",
                command.FrameId, command.VehicleId, reason);
            return UploadOutcome.Rejected(reason.Value);
        }
        var frameId = Guid.Parse(command.FrameId);

        // 4. Already saved (V9): no save, no event.
        if (await _repository.ReceivedFrameExistsAsync(frameId, ct))
        {
            _logger.LogInformation("Frame {FrameId} from vehicle {VehicleId} is a duplicate", frameId, command.VehicleId);
            return UploadOutcome.Of(UploadStatus.Duplicate);
        }

        // 5. Save the image (with retries and a circuit breaker inside the storage adapter, V13).
        var key = StorageKey(command.VehicleId, command.CapturedAt, frameId);
        string imageUrl;
        try
        {
            imageUrl = await _storage.SaveAsync(key, command.Image, FrameCheck.JpegContentType, ct);
        }
        catch (StorageUnavailableException e)
        {
            _logger.LogWarning("Frame {FrameId} from vehicle {VehicleId} not saved, storage unavailable: {Error}",
                frameId, command.VehicleId, e.InnerException?.Message ?? e.Message);
            return UploadOutcome.Of(UploadStatus.Unavailable);
        }

        // 6. One transaction: the received_frames row and FrameReceived in the outbox.
        await _publisher.PublishAsync(new FrameReceived
        {
            FrameId = frameId,
            CorrelationId = frameId,
            VehicleId = command.VehicleId,
            CapturedAt = command.CapturedAt,
            Location = new GeoLocation
            {
                Latitude = command.Latitude,
                Longitude = command.Longitude,
                AccuracyMeters = command.GpsAccuracyMeters,
                Heading = command.HeadingDeg
            },
            SpeedKmh = command.SpeedKmh,
            ImageUrl = imageUrl,
            ContentType = FrameCheck.JpegContentType,
            WidthPx = command.WidthPx,
            HeightPx = command.HeightPx
        }, ct);

        var frame = new ReceivedFrame(frameId, command.VehicleId, command.CapturedAt, now, key, command.Image.Length);
        if (!await _repository.SaveNewFrameAsync(frame, ct))
        {
            // The same frame arrived twice at the same moment and the other call saved it first.
            _logger.LogInformation("Frame {FrameId} from vehicle {VehicleId} was saved by a parallel call", frameId, command.VehicleId);
            return UploadOutcome.Of(UploadStatus.Duplicate);
        }

        _logger.LogInformation("Frame {FrameId} from vehicle {VehicleId} saved to {ImageUrl}", frameId, command.VehicleId, imageUrl);
        return UploadOutcome.Of(UploadStatus.Saved);
    }

    /// <summary>frames/{vehicleId}/{yyyy-MM-dd}/{frameId}.jpg, the date being the UTC capture day.</summary>
    public static string StorageKey(string vehicleId, DateTimeOffset capturedAt, Guid frameId) =>
        $"frames/{vehicleId}/{capturedAt.UtcDateTime:yyyy-MM-dd}/{frameId}.jpg";
}
