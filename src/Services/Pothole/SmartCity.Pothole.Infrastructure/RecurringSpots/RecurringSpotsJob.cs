using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartCity.Pothole.Infrastructure.Persistence;

namespace SmartCity.Pothole.Infrastructure.RecurringSpots;

/// <summary>
/// P17: finds the places where potholes come back and rewrites <c>recurring_spots</c> from scratch.
/// Runs once at startup and then every <see cref="RecurringSpotsOptions.IntervalHours"/>.
/// </summary>
public sealed class RecurringSpotsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RecurringSpotsOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RecurringSpotsJob> _logger;

    public RecurringSpotsJob(
        IServiceScopeFactory scopes,
        IOptions<RecurringSpotsOptions> options,
        TimeProvider time,
        ILogger<RecurringSpotsJob> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.IntervalHours));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The next run tries again; a failed run must not stop the service.
                _logger.LogError(e, "Recurring spots job failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PotholeDbContext>();
        var now = _time.GetUtcNow();
        var since = now.AddDays(-_options.LookbackDays);

        // ST_ClusterDBSCAN groups the potholes that are within RadiusMeters of each other. The locations are
        // turned into UTM zone 35N (EPSG:32635, which covers Istanbul) first, so the radius is in meters.
        // Potholes that are in no group get a NULL cluster id.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM recurring_spots", ct);
        var spots = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO recurring_spots (id, location, district_id, pothole_ids, pothole_count, first_seen_at, last_seen_at, computed_at)
            SELECT gen_random_uuid(),
                   ST_Centroid(ST_Collect(location::geometry))::geography,
                   (array_agg(district_id ORDER BY first_seen_at DESC))[1],
                   array_agg(id ORDER BY first_seen_at),
                   count(*),
                   min(first_seen_at),
                   max(last_seen_at),
                   {now}
            FROM (
                SELECT id, location, district_id, first_seen_at, last_seen_at,
                       ST_ClusterDBSCAN(ST_Transform(location::geometry, 32635),
                                        eps := {_options.RadiusMeters}, minpoints := {_options.MinPotholes}) OVER () AS cluster_id
                FROM potholes
                WHERE first_seen_at >= {since}
            ) grouped
            WHERE cluster_id IS NOT NULL
            GROUP BY cluster_id
            HAVING count(*) >= {_options.MinPotholes}
            """, ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Recurring spots rebuilt: {Spots} places with {MinPotholes}+ potholes within {RadiusMeters} m (last {LookbackDays} days)",
            spots, _options.MinPotholes, _options.RadiusMeters, _options.LookbackDays);
    }
}
