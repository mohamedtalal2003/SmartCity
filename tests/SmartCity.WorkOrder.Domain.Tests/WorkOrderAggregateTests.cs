using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;
using SmartCity.WorkOrder.Domain;
using Xunit;

namespace SmartCity.WorkOrder.Domain.Tests;

public class WorkOrderAggregateTests
{
    private static WorkOrderAggregate CreatePending(PriorityLevel priority = PriorityLevel.Medium)
    {
        return WorkOrderAggregate.Create(
            ticketNumber: "WO-2026-000001",
            sourceDetectionId: Guid.NewGuid(),
            sourceEntityId: Guid.NewGuid(),
            sourceEntityType: SourceEntityType.RoadDamage,
            correlationId: Guid.NewGuid(),
            location: new GeoLocation { Latitude = 37.87, Longitude = 32.49 },
            district: District.Meram,
            category: WorkOrderCategory.RoadRepair,
            priority: priority,
            title: "Pothole repair",
            description: "Pothole detected on main road");
    }

    [Fact]
    public void Create_sets_Pending_and_raises_created_event()
    {
        var wo = CreatePending();

        Assert.Equal(WorkOrderStatus.Pending, wo.Status);
        Assert.Single(wo.DomainEvents);
        Assert.IsType<WorkOrderCreatedDomainEvent>(wo.DomainEvents[0]);
    }

    [Theory]
    [InlineData(PriorityLevel.Critical, 24)]
    [InlineData(PriorityLevel.High, 48)]
    [InlineData(PriorityLevel.Medium, 120)]
    [InlineData(PriorityLevel.Routine, 240)]
    public void Create_sets_SLA_due_date_by_priority(PriorityLevel priority, int expectedHours)
    {
        var before = DateTimeOffset.UtcNow;
        var wo = CreatePending(priority);
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(wo.SlaDueAt);
        Assert.InRange(wo.SlaDueAt!.Value, before.AddHours(expectedHours), after.AddHours(expectedHours));
    }

    [Fact]
    public void Pending_to_Assigned_succeeds()
    {
        var wo = CreatePending();
        wo.ClearDomainEvents();

        wo.AssignCrew("CREW-01", "officer-1");

        Assert.Equal(WorkOrderStatus.Assigned, wo.Status);
        Assert.Equal("CREW-01", wo.AssignedCrewId);
        Assert.Single(wo.DomainEvents);
        Assert.IsType<WorkOrderStatusChangedDomainEvent>(wo.DomainEvents[0]);
    }

    [Fact]
    public void Assigned_to_EnRoute_succeeds()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");
        wo.ClearDomainEvents();

        wo.MarkEnRoute("crew-1");

        Assert.Equal(WorkOrderStatus.EnRoute, wo.Status);
        Assert.Single(wo.DomainEvents);
    }

    [Fact]
    public void EnRoute_to_OnSite_succeeds()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");
        wo.MarkEnRoute("crew-1");
        wo.ClearDomainEvents();

        wo.MarkOnSite("crew-1");

        Assert.Equal(WorkOrderStatus.OnSite, wo.Status);
    }

    [Fact]
    public void OnSite_to_Paused_succeeds()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");
        wo.MarkEnRoute("crew-1");
        wo.MarkOnSite("crew-1");
        wo.ClearDomainEvents();

        wo.Pause(PauseReason.Weather, "crew-1");

        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
        Assert.Equal(PauseReason.Weather, wo.CurrentPauseReason);
    }

    [Fact]
    public void Paused_to_OnSite_succeeds()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");
        wo.MarkEnRoute("crew-1");
        wo.MarkOnSite("crew-1");
        wo.Pause(PauseReason.Weather, "crew-1");
        wo.ClearDomainEvents();

        wo.Resume("crew-1");

        Assert.Equal(WorkOrderStatus.OnSite, wo.Status);
        Assert.Null(wo.CurrentPauseReason);
    }

    [Fact]
    public void OnSite_to_Completed_succeeds()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");
        wo.MarkEnRoute("crew-1");
        wo.MarkOnSite("crew-1");
        wo.ClearDomainEvents();

        wo.Complete("http://blob/proof.jpg", "crew-1");

        Assert.Equal(WorkOrderStatus.Completed, wo.Status);
        Assert.Equal("http://blob/proof.jpg", wo.CompletionProofUrl);
        Assert.NotNull(wo.CompletedAt);
    }

    [Fact]
    public void Assigned_to_Pending_unassign_succeeds()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");

        Assert.Equal(WorkOrderStatus.Assigned, wo.Status);
    }

    [Fact]
    public void History_is_appended_on_each_transition()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");
        wo.MarkEnRoute("crew-1");
        wo.MarkOnSite("crew-1");

        Assert.Equal(3, wo.StatusHistory.Count);
        Assert.Equal(WorkOrderStatus.Pending, wo.StatusHistory[0].From);
        Assert.Equal(WorkOrderStatus.Assigned, wo.StatusHistory[0].To);
        Assert.Equal(WorkOrderStatus.Assigned, wo.StatusHistory[1].From);
        Assert.Equal(WorkOrderStatus.EnRoute, wo.StatusHistory[1].To);
        Assert.Equal(WorkOrderStatus.EnRoute, wo.StatusHistory[2].From);
        Assert.Equal(WorkOrderStatus.OnSite, wo.StatusHistory[2].To);
    }

    [Theory]
    [InlineData(WorkOrderStatus.Pending, "MarkEnRoute")]
    [InlineData(WorkOrderStatus.Pending, "MarkOnSite")]
    [InlineData(WorkOrderStatus.Pending, "Pause")]
    [InlineData(WorkOrderStatus.Pending, "Complete")]
    [InlineData(WorkOrderStatus.Assigned, "MarkOnSite")]
    [InlineData(WorkOrderStatus.Assigned, "Pause")]
    [InlineData(WorkOrderStatus.Assigned, "Complete")]
    [InlineData(WorkOrderStatus.EnRoute, "Pause")]
    [InlineData(WorkOrderStatus.EnRoute, "Complete")]
    [InlineData(WorkOrderStatus.Paused, "Complete")]
    [InlineData(WorkOrderStatus.Paused, "MarkEnRoute")]
    public void Disallowed_transitions_throw(WorkOrderStatus startState, string action)
    {
        var wo = CreatePending();

        // Drive to the start state
        if (startState >= WorkOrderStatus.Assigned)
            wo.AssignCrew("CREW-01", "officer-1");
        if (startState >= WorkOrderStatus.EnRoute)
            wo.MarkEnRoute("crew-1");
        if (startState >= WorkOrderStatus.OnSite)
            wo.MarkOnSite("crew-1");
        if (startState == WorkOrderStatus.Paused)
            wo.Pause(PauseReason.Weather, "crew-1");

        Assert.Equal(startState, wo.Status);

        Assert.Throws<InvalidStateTransitionException>(() =>
        {
            switch (action)
            {
                case "MarkEnRoute": wo.MarkEnRoute("x"); break;
                case "MarkOnSite": wo.MarkOnSite("x"); break;
                case "Pause": wo.Pause(PauseReason.Other, "x"); break;
                case "Complete": wo.Complete("url", "x"); break;
            }
        });
    }

    [Fact]
    public void Completed_cannot_transition_further()
    {
        var wo = CreatePending();
        wo.AssignCrew("CREW-01", "officer-1");
        wo.MarkEnRoute("crew-1");
        wo.MarkOnSite("crew-1");
        wo.Complete("url", "crew-1");

        Assert.Throws<InvalidStateTransitionException>(() => wo.AssignCrew("CREW-02", "x"));
        Assert.Throws<InvalidStateTransitionException>(() => wo.MarkEnRoute("x"));
        Assert.Throws<InvalidStateTransitionException>(() => wo.MarkOnSite("x"));
        Assert.Throws<InvalidStateTransitionException>(() => wo.Pause(PauseReason.Other, "x"));
        Assert.Throws<InvalidStateTransitionException>(() => wo.Complete("url2", "x"));
    }

    [Fact]
    public void AttachCostEstimate_sets_amount()
    {
        var wo = CreatePending();

        wo.AttachCostEstimate(2500m);

        Assert.Equal(2500m, wo.EstimatedCost);
    }
}
