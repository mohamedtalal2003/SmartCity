using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartCity.Pothole.Application.Commands;
using SmartCity.Pothole.Application.Detections;
using SmartCity.Pothole.Application.Officers;
using SmartCity.Pothole.Application.WorkOrders;
using SmartCity.Pothole.Domain.Estimation;

namespace SmartCity.Pothole.Application;

public static class DependencyInjection
{
    /// <summary>Registers the Pothole use cases and their settings.</summary>
    public static IServiceCollection AddPotholeApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DetectionOptions>(configuration.GetSection(DetectionOptions.SectionName));

        // The prices only come from appsettings, so fail at startup if the section is missing.
        services.AddOptions<PotholePrices>()
            .Bind(configuration.GetSection("PotholePrices"))
            .Validate(p => p.Low > 0 && p.Medium > 0 && p.High > 0 && p.Critical > 0 && p.Currency != "",
                "The PotholePrices section in appsettings is missing or incomplete.")
            .ValidateOnStart();

        // Officer commands only accept photos from our own blob storage.
        services.AddOptions<BlobStorageOptions>()
            .Bind(configuration.GetSection(BlobStorageOptions.SectionName))
            .Validate(o => Uri.IsWellFormedUriString(o.PublicBaseUrl, UriKind.Absolute),
                "S3:PublicBaseUrl in appsettings is missing or not a URL.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<HandleDetection>();
        services.AddScoped<ApplyWorkOrderUpdate>();
        services.AddScoped<AddPotholeByOfficer>();
        services.AddScoped<CorrectPothole>();

        return services;
    }
}
