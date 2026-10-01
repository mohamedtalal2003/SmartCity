using MassTransit;
using SmartCity.Contracts.Events;
using LogContext = Serilog.Context.LogContext;

namespace SmartCity.BuildingBlocks;

/// <summary>
/// Pushes the consumed event's CorrelationId (and EventId, MessageType) into the Serilog log context,
/// so every log line written while handling a message can be found with <c>CorrelationId = '&lt;frame id&gt;'</c>.
/// </summary>
public sealed class CorrelationIdLogFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var integrationEvent = context.Message as IntegrationEvent;
        var correlationId = integrationEvent?.CorrelationId ?? context.CorrelationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("EventId", integrationEvent?.EventId ?? context.MessageId))
        using (LogContext.PushProperty("MessageType", typeof(T).Name))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationIdLog");
}
