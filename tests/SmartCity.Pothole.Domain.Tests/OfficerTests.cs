using SmartCity.Contracts.Enums;
using Xunit;

namespace SmartCity.Pothole.Domain.Tests;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>Officer adds (P1) and corrections (P2).</summary>
public class OfficerTests
{
    [Fact]
    public void Officer_add_makes_an_Open_pothole_with_no_AI_sightings_and_an_OfficerAdd_observation()
    {
        var report = TestData.OfficerReport(SeverityLevel.High, PotholeSource.CitizenReport);

        var pothole = Pothole.AddedByOfficer(report, districtId: 1, neighborhoodId: 2, TestData.Prices);

        Assert.Equal(PotholeStatus.Open, pothole.Status);
        Assert.Equal(PotholeSource.CitizenReport, pothole.Source);
        Assert.Equal(0, pothole.SightingsCount);
        Assert.Equal(SeverityLevel.High, pothole.CurrentSeverity);
        Assert.Equal(DetectionType.Pothole, pothole.DamageType);
        Assert.Equal(0.4, pothole.AreaM2);
        Assert.Equal(8, pothole.DepthCm);
        Assert.Equal(2500m, pothole.Estimate.Total);
        var observation = Assert.Single(pothole.Observations);
        Assert.Equal(ObservationKind.OfficerAdd, observation.Kind);
        Assert.Null(observation.SourceDetectionId);
        Assert.Equal("officer-7", observation.CreatedBy);
        Assert.Equal(report.ImageUrl, observation.ImageUrl);
    }

    [Fact]
    public void Officer_add_with_the_source_Ai_is_rejected()
    {
        var report = TestData.OfficerReport(source: PotholeSource.Ai);

        Assert.Throws<DomainException>(() => Pothole.AddedByOfficer(report, 1, 2, TestData.Prices));
    }

    [Fact]
    public void Officer_add_with_a_zero_depth_is_rejected()
    {
        var report = TestData.OfficerReport() with { DepthCm = 0 };

        Assert.Throws<DomainException>(() => Pothole.AddedByOfficer(report, 1, 2, TestData.Prices));
    }

    [Fact]
    public void AddReport_only_adds_an_observation()
    {
        var pothole = TestData.NewPothole(severity: SeverityLevel.Medium);
        var lastSeen = pothole.LastSeenAt;

        pothole.AddReport(TestData.OfficerReport(SeverityLevel.Critical));

        Assert.Equal(2, pothole.Observations.Count);
        Assert.Equal(ObservationKind.OfficerAdd, pothole.Observations[^1].Kind);
        Assert.Equal(SeverityLevel.Medium, pothole.CurrentSeverity);
        Assert.Equal(1, pothole.SightingsCount);
        Assert.Equal(lastSeen, pothole.LastSeenAt);
    }

    [Fact]
    public void AddReport_on_a_closed_pothole_throws()
    {
        var pothole = TestData.NewPothole();
        pothole.Close(ClosedReason.Repaired, null, DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => pothole.AddReport(TestData.OfficerReport()));
    }

    [Fact]
    public void Correction_may_lower_the_severity_and_recomputes_the_estimate()
    {
        var pothole = TestData.NewPothole(severity: SeverityLevel.Critical);
        var rowVersion = pothole.RowVersion;

        pothole.Correct(TestData.Correction(severity: SeverityLevel.Low, depthCm: 2), TestData.Prices);

        Assert.Equal(SeverityLevel.Low, pothole.CurrentSeverity);
        Assert.Equal(2, pothole.DepthCm);
        Assert.Equal(1000m, pothole.Estimate.Total);
        Assert.Equal(rowVersion + 1, pothole.RowVersion);
        var observation = pothole.Observations[^1];
        Assert.Equal(ObservationKind.Correction, observation.Kind);
        Assert.Equal(SeverityLevel.Low, observation.Severity);
        Assert.Equal("Checked on site", observation.Notes);
        Assert.Equal("officer-7", observation.CreatedBy);
        Assert.Null(observation.ImageUrl);
    }

    [Fact]
    public void Correction_keeps_the_values_that_were_not_given()
    {
        var pothole = TestData.NewPothole(severity: SeverityLevel.High);

        pothole.Correct(TestData.Correction(areaM2: 1.2), TestData.Prices);

        Assert.Equal(1.2, pothole.AreaM2);
        Assert.Equal(SeverityLevel.High, pothole.CurrentSeverity);
        Assert.Equal(DamageStage.Pothole, pothole.CurrentStage);
        Assert.Equal(5, pothole.DepthCm);
    }

    [Fact]
    public void Correction_on_a_closed_pothole_throws()
    {
        var pothole = TestData.NewPothole();
        pothole.Close(ClosedReason.FalsePositive, null, DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() =>
            pothole.Correct(TestData.Correction(severity: SeverityLevel.Low), TestData.Prices));
    }

    [Fact]
    public void Correction_without_a_reason_throws()
    {
        var pothole = TestData.NewPothole();

        Assert.Throws<DomainException>(() =>
            pothole.Correct(TestData.Correction(severity: SeverityLevel.Low, reason: " "), TestData.Prices));
    }

    [Fact]
    public void Correction_that_changes_nothing_throws()
    {
        var pothole = TestData.NewPothole(severity: SeverityLevel.Medium);

        Assert.Throws<DomainException>(() =>
            pothole.Correct(TestData.Correction(severity: SeverityLevel.Medium, depthCm: 5), TestData.Prices));
        Assert.Single(pothole.Observations);
    }
}
