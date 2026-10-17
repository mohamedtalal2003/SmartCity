using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.VehicleCommunication.Application.Abstractions;

namespace SmartCity.VehicleCommunication.Application.Fleet;

/// <summary>
/// Whether the vehicles may upload now (V13, V14). One instance for the whole service.
/// Paused when the frame storage's circuit breaker opens; running again when the storage works.
/// Every change is published as the retained fleet/status message.
/// </summary>
public sealed class FleetStatus
{
    public const string StorageReason = "storage";

    private readonly IVehicleMessenger _messenger;
    private readonly FleetOptions _options;
    private readonly ILogger<FleetStatus> _logger;
    private int _paused;    // 0 = running, 1 = paused (changed with Interlocked: uploads run in parallel)

    public FleetStatus(IVehicleMessenger messenger, IOptions<FleetOptions> options, ILogger<FleetStatus> logger)
    {
        _messenger = messenger;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsPaused => Volatile.Read(ref _paused) == 1;

    /// <summary>Tell the vehicles to stop uploading. Nothing happens if already paused.</summary>
    public async Task PauseAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _paused, 1) == 1)
            return;

        _logger.LogWarning("Frame storage is down: telling the vehicles to pause uploads");
        await PublishCurrentAsync(ct);
    }

    /// <summary>Tell the vehicles to upload again. Nothing happens if already running.</summary>
    public async Task ResumeAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _paused, 0) == 0)
            return;

        _logger.LogInformation("Frame storage works again: telling the vehicles to resume uploads");
        await PublishCurrentAsync(ct);
    }

    /// <summary>
    /// Publishes the current state (at startup, and after every MQTT reconnect in case a change was missed).
    /// A failure is only logged: the next reconnect publishes it again.
    /// </summary>
    public async Task PublishCurrentAsync(CancellationToken ct)
    {
        var message = IsPaused
            ? FleetStatusMessage.Paused(StorageReason, _options.RetryAfterSeconds)
            : FleetStatusMessage.Running;
        try
        {
            await _messenger.PublishFleetStatusAsync(message, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("Could not publish fleet/status {State}: {Error}", message.State, e.Message);
        }
    }
}
