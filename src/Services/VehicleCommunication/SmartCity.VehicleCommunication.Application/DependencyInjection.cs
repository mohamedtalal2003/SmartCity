using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<FrameRateLimiter>();

        services.AddScoped<UploadFrame>();
        services.AddScoped<AddVehicle>();
        services.AddScoped<UpdateVehicle>();
        services.AddScoped<ChangeVehicleStatus>();

        return services;
    }
}
