using Microsoft.Extensions.Options;
using SmartCity.VehicleCommunication.Application.Connections;
using SmartCity.VehicleCommunication.Application.Fleet;

namespace SmartCity.VehicleCommunication.Api.Jobs;

/// <summary>Every 30 s (config Fleet:OfflineSweepSeconds): Online vehicles with no heartbeat for 60 s become Offline (V18).</summary>
public sealed class OfflineSweepJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeSpan _interval;
    private readonly ILogger<OfflineSweepJob> _logger;

    public OfflineSweepJob(IServiceScopeFactory scopes, IOptions<FleetOptions> options, ILogger<OfflineSweepJob> logger)
    {
        _scopes = scopes;
        _interval = TimeSpan.FromSeconds(options.Value.OfflineSweepSeconds);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MarkSilentVehiclesOffline>().HandleAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(e, "Offline sweep failed; trying again next time");
            }
        }
    }
}
