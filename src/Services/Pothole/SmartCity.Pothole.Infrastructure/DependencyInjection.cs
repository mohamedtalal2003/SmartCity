using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartCity.Pothole.Application.Abstractions;
using SmartCity.Pothole.Infrastructure.Messaging;
using SmartCity.Pothole.Infrastructure.Persistence;

namespace SmartCity.Pothole.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database (pothole_db with PostGIS), the repository, the event publisher and the
    /// area seeder. The bus itself (and the outbox) is set up in the Api's Program.cs.
    /// </summary>
    public static IServiceCollection AddPotholeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PotholeDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Db"), npgsql => npgsql.UseNetTopologySuite()));

        services.AddScoped<IPotholeRepository, PotholeRepository>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddScoped<AdminAreaSeeder>();

        return services;
    }
}
