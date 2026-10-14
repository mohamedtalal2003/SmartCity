using SmartCity.Contracts.Enums;
using Xunit;

namespace SmartCity.Pothole.Domain.Tests;

using SmartCity.Pothole.Domain.Potholes;

public class PotholeTests
{
    [Fact]
    public void New_detection_makes_an_Open_pothole_with_one_observation_and_an_estimate()
    {
        var detection = TestData.Detection(DetectionType.Pothole, SeverityLevel.High);

        var pothole = Pothole.FromDetection(detection, districtId: 1, neighborhoodId: 2, TestData.Prices);

        Assert.Equal(PotholeStatus.Open, pothole.Status);
        Assert.Equal(PotholeSource.Ai, pothole.Source);
        Assert.Equal(1, pothole.SightingsCount);
        Assert.Equal(1, pothole.DistrictId);
        Assert.Equal(2, pothole.NeighborhoodId);
        var observation = Assert.Single(pothole.Observations);
        Assert.Equal(ObservationKind.AiDetection, observation.Kind);
        Assert.Equal(detection.DetectionId, observation.SourceDetectionId);
        Assert.Equal(pothole.Id, observation.PotholeId);
        Assert.Equal(2500m, pothole.Estimate.Total);
        Assert.Equal("TRY", pothole.Estimate.Currency);
    }

    [Fact]
    public void Sighting_with_a_higher_severity_returns_true_and_updates_the_estimate()
    {
        var pothole = TestData.NewPothole(severity: SeverityLevel.Medium);

        var changed = pothole.AddSighting(TestData.Detection(severity: SeverityLevel.Critical), TestData.Prices);

        Assert.True(changed);
        Assert.Equal(SeverityLevel.Critical, pothole.CurrentSeverity);
        Assert.Equal(4000m, pothole.Estimate.Total);
        Assert.Equal(2, pothole.SightingsCount);
        Assert.Equal(2, pothole.Observations.Count);
    }

    [Fact]
    public void Sighting_with_the_same_severity_returns_false_but_still_adds_the_observation()
    {
        var pothole = TestData.NewPothole(severity: SeverityLevel.Medium);
        var later = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

        var changed = pothole.AddSighting(TestData.Detection(severity: SeverityLevel.Medium, seenAt: later), TestData.Prices);

        Assert.False(changed);
        Assert.Equal(2, pothole.SightingsCount);
        Assert.Equal(2, pothole.Observations.Count);
        Assert.Equal(later, pothole.LastSeenAt);
    }

    [Fact]
    public void Crack_then_pothole_sighting_becomes_a_pothole_and_returns_true()
    {
        var pothole = TestData.NewPothole(type: DetectionType.RoadCrack);

        var changed = pothole.AddSighting(TestData.Detection(type: DetectionType.Pothole), TestData.Prices);

        Assert.True(changed);
        Assert.Equal(DetectionType.Pothole, pothole.DamageType);
        Assert.Equal(DamageStage.Pothole, pothole.CurrentStage);
    }

    [Fact]
    public void Pothole_then_crack_sighting_stays_a_pothole()
    {
        var pothole = TestData.NewPothole(type: DetectionType.Pothole);

        var changed = pothole.AddSighting(TestData.Detection(type: DetectionType.RoadCrack), TestData.Prices);

        Assert.False(changed);
        Assert.Equal(DetectionType.Pothole, pothole.DamageType);
        Assert.Equal(DamageStage.Pothole, pothole.CurrentStage);
        // The observation keeps its own type, so the timeline shows what the AI saw.
        Assert.Equal(DetectionType.RoadCrack, pothole.Observations[^1].DamageType);
    }

    [Fact]
    public void Closed_pothole_rejects_AddSighting()
    {
        var pothole = TestData.NewPothole();
        pothole.Close(ClosedReason.Repaired, null, DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => pothole.AddSighting(TestData.Detection(), TestData.Prices));
    }

    [Fact]
    public void AddSuppressedSighting_adds_the_observation_and_does_not_reopen()
    {
        var pothole = TestData.NewPothole();
        pothole.Close(ClosedReason.FalsePositive, null, DateTimeOffset.UtcNow);

        pothole.AddSuppressedSighting(TestData.Detection());

        Assert.Equal(PotholeStatus.Closed, pothole.Status);
        Assert.Equal(ClosedReason.FalsePositive, pothole.ClosedReason);
        Assert.Equal(1, pothole.SightingsCount);
        Assert.Equal(2, pothole.Observations.Count);
    }

    [Fact]
    public void AddSuppressedSighting_on_an_open_pothole_throws()
    {
        var pothole = TestData.NewPothole();

        Assert.Throws<DomainException>(() => pothole.AddSuppressedSighting(TestData.Detection()));
    }

    [Fact]
    public void Close_twice_is_a_no_op()
    {
        var pothole = TestData.NewPothole();
        var firstClose = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
        pothole.Close(ClosedReason.Repaired, null, firstClose);
        var rowVersion = pothole.RowVersion;

        pothole.Close(ClosedReason.FalsePositive, null, firstClose.AddDays(1));

        Assert.Equal(ClosedReason.Repaired, pothole.ClosedReason);
        Assert.Equal(firstClose, pothole.ClosedAt);
        Assert.Equal(rowVersion, pothole.RowVersion);
    }

    [Fact]
    public void Close_as_Duplicate_without_an_id_throws()
    {
        var pothole = TestData.NewPothole();

        Assert.Throws<DomainException>(() => pothole.Close(ClosedReason.Duplicate, null, DateTimeOffset.UtcNow));
        Assert.Equal(PotholeStatus.Open, pothole.Status);
    }

    [Fact]
    public void Close_as_Duplicate_of_itself_throws()
    {
        var pothole = TestData.NewPothole();

        Assert.Throws<DomainException>(() => pothole.Close(ClosedReason.Duplicate, pothole.Id, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Close_as_Duplicate_keeps_the_other_id()
    {
        var pothole = TestData.NewPothole();
        var otherId = Guid.NewGuid();

        pothole.Close(ClosedReason.Duplicate, otherId, DateTimeOffset.UtcNow);

        Assert.Equal(otherId, pothole.DuplicateOfId);
    }

    [Fact]
    public void ApplyRepairStatus_ignores_an_older_sequence()
    {
        var pothole = TestData.NewPothole();
        var workOrderId = Guid.NewGuid();
        pothole.ApplyRepairStatus(workOrderId, "OnSite", sequence: 20);

        pothole.ApplyRepairStatus(workOrderId, "Assigned", sequence: 10);

        Assert.Equal("OnSite", pothole.RepairStatus);
        Assert.Equal(20, pothole.RepairStatusSeq);
        Assert.Equal(workOrderId, pothole.WorkOrderId);
    }

    [Fact]
    public void ApplyRepairStatus_is_ignored_after_Close()
    {
        var pothole = TestData.NewPothole();
        pothole.ApplyRepairStatus(Guid.NewGuid(), "Completed", sequence: 30);
        pothole.Close(ClosedReason.Repaired, null, DateTimeOffset.UtcNow);

        pothole.ApplyRepairStatus(Guid.NewGuid(), "Pending", sequence: 40);

        Assert.Equal("Completed", pothole.RepairStatus);
        Assert.Equal(30, pothole.RepairStatusSeq);
    }

    [Fact]
    public void Every_change_increases_RowVersion()
    {
        var pothole = TestData.NewPothole();
        var start = pothole.RowVersion;

        pothole.AddSighting(TestData.Detection(), TestData.Prices);
        pothole.ApplyRepairStatus(Guid.NewGuid(), "Pending", sequence: 1);
        pothole.Close(ClosedReason.Repaired, null, DateTimeOffset.UtcNow);

        Assert.Equal(start + 3, pothole.RowVersion);
    }
}
