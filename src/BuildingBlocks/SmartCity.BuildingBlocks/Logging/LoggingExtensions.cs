using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace SmartCity.BuildingBlocks;

public static class LoggingExtensions
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Serilog to console + Seq (<c>Seq:ServerUrl</c>, default http://localhost:5341). Every line carries
    /// a <c>Service</c> property; consumers add <c>CorrelationId</c> (see <see cref="CorrelationIdLogFilter{T}"/>).
    /// Levels can be overridden with a standard <c>Serilog</c> config section.
    /// </summary>
    public static WebApplicationBuilder AddSmartCityLogging(this WebApplicationBuilder builder, string serviceName)
    {
        var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341";

        builder.Services.AddSerilog((services, log) => log
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
            .WriteTo.Seq(seqUrl));

        return builder;
    }
}
