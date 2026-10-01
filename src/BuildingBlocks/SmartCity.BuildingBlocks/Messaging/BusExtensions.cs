using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using SmartCity.Contracts.Events;

namespace SmartCity.BuildingBlocks;

public static class BusExtensions
{
    /// <summary>
    /// MassTransit 8 on RabbitMQ (<c>RabbitMQ:Host</c>, <c>VirtualHost</c>, <c>Username</c>, <c>Password</c>).
    /// Queues are kebab-case and prefixed with the service name, one per consumer
    /// (e.g. <c>pothole-raw-detection</c>). Every receive endpoint retries 1 s, 2 s, 4 s, then moves the
    /// message to its <c>_error</c> queue (our DLQ). Register consumers, and
    /// <see cref="OutboxExtensions.AddSmartCityOutbox{TDbContext}"/> for services with a database,
    /// in <paramref name="configure"/>.
    /// </summary>
    public static WebApplicationBuilder AddSmartCityBus(
        this WebApplicationBuilder builder, string serviceName, Action<IBusRegistrationConfigurator>? configure = null)
    {
        var rabbit = builder.Configuration.GetSection("RabbitMQ");

        builder.Services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, false));

            x.ConfigureHealthCheckOptions(o =>
            {
                o.Name = "rabbitmq";
                o.Tags.Add(HealthCheckExtensions.ReadyTag);
            });

            // Registered before `configure` so these filters wrap the EF inbox/outbox that
            // AddSmartCityOutbox adds: log context first, then retry around the whole transaction.
            x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseConsumeFilter(typeof(CorrelationIdLogFilter<>), context);
                endpoint.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
            });

            configure?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbit["Host"] ?? "localhost", rabbit["VirtualHost"] ?? "/", h =>
                {
                    h.Username(Required(rabbit, "Username"));
                    h.Password(Required(rabbit, "Password"));
                });

                // Wire shape = src/SmartCity.Contracts/MockMessages/mock-events.json:
                // PascalCase property names, enums as strings.
                cfg.ConfigureJsonSerializerOptions(json =>
                {
                    json.PropertyNamingPolicy = null;
                    json.Converters.Add(new JsonStringEnumConverter());
                    return json;
                });

                // No catch-all exchange for the base type.
                cfg.Publish<IntegrationEvent>(p => p.Exclude = true);

                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }

    private static string Required(IConfiguration section, string key) =>
        section[key] ?? throw new InvalidOperationException($"Missing configuration value RabbitMQ:{key}");
}
