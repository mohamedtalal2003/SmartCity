using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartCity.BuildingBlocks;
using SmartCity.VehicleCommunication.Application.Abstractions;
using SmartCity.VehicleCommunication.Infrastructure.LiveMap;
using SmartCity.VehicleCommunication.Infrastructure.Messaging;
using SmartCity.VehicleCommunication.Infrastructure.Mqtt;
using SmartCity.VehicleCommunication.Infrastructure.Persistence;
using SmartCity.VehicleCommunication.Infrastructure.Storage;
using StackExchange.Redis;

namespace SmartCity.VehicleCommunication.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database (vehiclecomm_db), the repository, the event publishers, the frame storage, MQTT, the
    /// Redis live map and the dev seeder. The bus itself (and the outbox) is set up in the Api's Program.cs.
    /// </summary>
    public static IServiceCollection AddVehicleCommInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<VehicleCommDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Db")));

        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddScoped<DevVehicleSeeder>();

        // One instance, so the circuit breaker sees every upload.
        services.AddOptions<S3Options>().Bind(configuration.GetSection("S3"));
        services.Configure<FrameStorageOptions>(configuration.GetSection(FrameStorageOptions.SectionName));
        services.AddSingleton<IFrameStorage, S3FrameStorage>();

        // One MQTT connection for the whole service: receiving (Api's MqttListener) and sending.
        services.Configure<MqttOptions>(configuration.GetSection(MqttOptions.SectionName));
        services.AddSingleton<MqttConnection>();
        services.AddSingleton<IVehicleMessenger, MqttVehicleMessenger>();

        // Redis connects in the background (AbortOnConnectFail = false), so the service starts even if Redis is late.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redis = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"] ?? "localhost:6379");
            redis.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redis);
        });
        services.AddSingleton<ILiveMap, RedisLiveMap>();
        services.AddSingleton<ITelemetryPublisher, BusTelemetryPublisher>();

        return services;
    }
}
