using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.WorkOrder.Api.Consumers;
using SmartCity.WorkOrder.Api.Data;
using SmartCity.WorkOrder.Api.Domain;
using SmartCity.WorkOrder.Domain;
using Xunit;

namespace SmartCity.WorkOrder.Consumer.Tests;

public class WorkOrderCostReconciliationConsumerTests : IDisposable
{
    private readonly WorkOrderDbContext _db;

    public WorkOrderCostReconciliationConsumerTests()
    {
        var options = new DbContextOptionsBuilder<WorkOrderDbContext>()
            .UseInMemoryDatabase($"reconciliation-{Guid.NewGuid()}")
            .Options;
        _db = new WorkOrderDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Reconciles_pending_cost_to_work_order_without_cost()
    {
        var entityId = Guid.NewGuid();
        var wo = CreateWorkOrder(entityId, estimatedCost: null);
        _db.WorkOrders.Add(wo);
        _db.PendingCostEstimates.Add(new PendingCostEstimate
        {
            Id = Guid.NewGuid(),
            SourceEntityId = entityId,
            TotalEstimatedCost = 5000m,
            ReceivedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var consumer = new WorkOrderCostReconciliationConsumer(_db);
        var context = MakeContext(new WorkOrderCreated
        {
            WorkOrderId = wo.Id,
            SourceEntityId = entityId,
            EstimatedCost = null,
        });

        await consumer.Consume(context);

        var updated = await _db.WorkOrders.FindAsync(wo.Id);
        Assert.Equal(5000m, updated!.EstimatedCost);
        Assert.Empty(await _db.PendingCostEstimates.ToListAsync());
    }

    [Fact]
    public async Task Noop_when_event_already_has_cost()
    {
        var entityId = Guid.NewGuid();
        var wo = CreateWorkOrder(entityId, estimatedCost: null);
        _db.WorkOrders.Add(wo);
        _db.PendingCostEstimates.Add(new PendingCostEstimate
        {
            Id = Guid.NewGuid(),
            SourceEntityId = entityId,
            TotalEstimatedCost = 5000m,
            ReceivedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var consumer = new WorkOrderCostReconciliationConsumer(_db);
        var context = MakeContext(new WorkOrderCreated
        {
            WorkOrderId = wo.Id,
            SourceEntityId = entityId,
            EstimatedCost = 3000m,
        });

        await consumer.Consume(context);

        var updated = await _db.WorkOrders.FindAsync(wo.Id);
        Assert.Null(updated!.EstimatedCost);
        Assert.Single(await _db.PendingCostEstimates.ToListAsync());
    }

    [Fact]
    public async Task Noop_when_work_order_already_has_cost()
    {
        var entityId = Guid.NewGuid();
        var wo = CreateWorkOrder(entityId, estimatedCost: 2000m);
        _db.WorkOrders.Add(wo);
        _db.PendingCostEstimates.Add(new PendingCostEstimate
        {
            Id = Guid.NewGuid(),
            SourceEntityId = entityId,
            TotalEstimatedCost = 5000m,
            ReceivedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var consumer = new WorkOrderCostReconciliationConsumer(_db);
        var context = MakeContext(new WorkOrderCreated
        {
            WorkOrderId = wo.Id,
            SourceEntityId = entityId,
            EstimatedCost = null,
        });

        await consumer.Consume(context);

        var updated = await _db.WorkOrders.FindAsync(wo.Id);
        Assert.Equal(2000m, updated!.EstimatedCost);
        Assert.Single(await _db.PendingCostEstimates.ToListAsync());
    }

    [Fact]
    public async Task Noop_when_no_pending_estimate_exists()
    {
        var entityId = Guid.NewGuid();
        var wo = CreateWorkOrder(entityId, estimatedCost: null);
        _db.WorkOrders.Add(wo);
        await _db.SaveChangesAsync();

        var consumer = new WorkOrderCostReconciliationConsumer(_db);
        var context = MakeContext(new WorkOrderCreated
        {
            WorkOrderId = wo.Id,
            SourceEntityId = entityId,
            EstimatedCost = null,
        });

        await consumer.Consume(context);

        var updated = await _db.WorkOrders.FindAsync(wo.Id);
        Assert.Null(updated!.EstimatedCost);
    }

    private static WorkOrderAggregate CreateWorkOrder(Guid sourceEntityId, decimal? estimatedCost)
    {
        var wo = WorkOrderAggregate.Create(
            ticketNumber: "WO-2026-000001",
            sourceDetectionId: Guid.NewGuid(),
            sourceEntityId: sourceEntityId,
            sourceEntityType: SourceEntityType.RoadDamage,
            correlationId: Guid.NewGuid(),
            location: new GeoLocation { Latitude = 37.87, Longitude = 32.49 },
            district: District.Meram,
            category: WorkOrderCategory.RoadRepair,
            priority: PriorityLevel.Medium,
            title: "Test pothole",
            description: "Test",
            estimatedCost: estimatedCost);
        wo.ClearDomainEvents();
        return wo;
    }

    private static ConsumeContext<WorkOrderCreated> MakeContext(WorkOrderCreated msg)
    {
        var ctx = Substitute.For<ConsumeContext<WorkOrderCreated>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }
}
