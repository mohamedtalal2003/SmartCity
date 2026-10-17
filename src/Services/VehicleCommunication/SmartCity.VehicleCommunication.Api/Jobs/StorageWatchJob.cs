using Microsoft.Extensions.Options;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Application.Fleet;

namespace SmartCity.VehicleCommunication.Api.Jobs;

/// <summary>
/// V14: while uploads are paused, check the frame storage every 10 s (config Fleet:StorageCheckSeconds). When it
/// works again, tell the vehicles to resume (retained {"state":"running"}). The pause itself is started by the
/// storage's circuit breaker (V13).
/// </summary>
public sealed class StorageWatchJob : BackgroundService
{
    private readonly FleetStatus _fleetStatus;
    private readonly IFrameStorage _storage;
    private readonly TimeSpan _interval;
    private readonly ILogger<StorageWatchJob> _logger;

    public StorageWatchJob(FleetStatus fleetStatus, IFrameStorage storage, IOptions<FleetOptions> options, ILogger<StorageWatchJob> logger)
    {
        _fleetStatus = fleetStatus;
        _storage = storage;
        _interval = TimeSpan.FromSeconds(options.Value.StorageCheckSeconds);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_fleetStatus.IsPaused)
                continue;

            try
            {
                if (await _storage.IsHealthyAsync(stoppingToken))
                    await _fleetStatus.ResumeAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(e, "Storage check failed; trying again next time");
            }
        }
    }
}
