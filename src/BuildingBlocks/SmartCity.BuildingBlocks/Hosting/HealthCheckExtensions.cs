using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SmartCity.BuildingBlocks;

public static class HealthCheckExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers the readiness checks: Postgres when <c>ConnectionStrings:Db</c> is set. The RabbitMQ
    /// check comes from <see cref="BusExtensions.AddSmartCityBus"/> (MassTransit's bus health, tagged ready).
    /// </summary>
    public static WebApplicationBuilder AddSmartCityHealthChecks(this WebApplicationBuilder builder)
    {
        var checks = builder.Services.AddHealthChecks();

        var db = builder.Configuration.GetConnectionString("Db");
        if (!string.IsNullOrWhiteSpace(db))
        {
            // An unreachable (not refusing) database must fail readiness fast. Npgsql does not stop on the
            // health-check cancellation, so give the probe its own short connect and command timeouts
            // (defaults 15 s / 30 s). The service's own DbContext keeps the normal connection string.
            var probe = new NpgsqlConnectionStringBuilder(db) { Timeout = 3, CommandTimeout = 3 }.ConnectionString;
            checks.AddNpgSql(probe, name: "postgres", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        }

        return builder;
    }

    /// <summary><c>/health/live</c>: the process is up (no checks). <c>/health/ready</c>: all ready checks pass.</summary>
    public static WebApplication MapSmartCityHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
        return app;
    }
}
