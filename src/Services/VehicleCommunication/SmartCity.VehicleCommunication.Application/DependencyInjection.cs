using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartCity.VehicleCommunication.Application.Connections;
using SmartCity.VehicleCommunication.Application.Fleet;
using SmartCity.VehicleCommunication.Application.Frames;
using SmartCity.VehicleCommunication.Application.Vehicles;
using SmartCity.VehicleCommunication.Domain.Frames;

namespace SmartCity.VehicleCommunication.Application;

public static class DependencyInjection
{
    /// <summary>Registers the Vehicle Communication use cases and their settings.</summary>
    public static IServiceCollection AddVehicleCommApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FrameCheckOptions>(configuration.GetSection("FrameCheck"));
        services.Configure<IngestOptions>(configuration.GetSection(IngestOptions.SectionName));
        services.Configure<FleetOptions>(configuration.GetSection(FleetOptions.SectionName));
        services.Configure<CaptureOptions>(configuration.GetSection(CaptureOptions.SectionName));

        services.AddSingleton(TimeProvider.System);

        // One instance each: they keep state for the whole service.
        services.AddSingleton<FrameRateLimiter>();
        services.AddSingleton<HeartbeatThrottle>();
        services.AddSingleton<FleetStatus>();

        // Frames (gRPC)
        services.AddScoped<UploadFrame>();

        // Admin commands (gRPC)
        services.AddScoped<AddVehicle>();
        services.AddScoped<UpdateVehicle>();
        services.AddScoped<ChangeVehicleStatus>();

        // MQTT and jobs
        services.AddScoped<RecordTelemetry>();
        services.AddScoped<UpdateConnection>();
        services.AddScoped<MarkSilentVehiclesOffline>();
        services.AddScoped<AnnounceToFleet>();

        return services;
    }
}
