using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace SmartCity.BuildingBlocks;

public static class OutboxExtensions
{
    /// <summary>
    /// MassTransit EF Core transactional outbox + inbox on Postgres. Call inside
    /// <see cref="BusExtensions.AddSmartCityBus"/>'s configure callback, after registering the DbContext.
    /// <list type="bullet">
    /// <item>Consumers: the inbox de-duplicates redeliveries by MessageId, and events published from the
    /// consumer commit in the same transaction as the consumer's SaveChanges.</item>
    /// <item>gRPC write handlers: <c>IPublishEndpoint</c> resolved in the request scope writes to the
    /// outbox (bus outbox); call <c>SaveChangesAsync</c> once to commit the row and the event together.</item>
    /// </list>
    /// The DbContext must call <see cref="AddSmartCityOutboxEntities"/> in <c>OnModelCreating</c>.
    /// </summary>
    public static IBusRegistrationConfigurator AddSmartCityOutbox<TDbContext>(this IBusRegistrationConfigurator x)
        where TDbContext : DbContext
    {
        x.AddEntityFrameworkOutbox<TDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
        });

        x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            endpoint.UseEntityFrameworkOutbox<TDbContext>(context));

        return x;
    }

    /// <summary>Adds the inbox_state, outbox_message and outbox_state tables to a service's model.</summary>
    public static ModelBuilder AddSmartCityOutboxEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        return modelBuilder;
    }
}
