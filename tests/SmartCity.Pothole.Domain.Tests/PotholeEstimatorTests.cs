using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Domain.Estimation;
using Xunit;

namespace SmartCity.Pothole.Domain.Tests;

public class PotholeEstimatorTests
{
    [Theory]
    [InlineData(SeverityLevel.Low, 1000)]
    [InlineData(SeverityLevel.Medium, 1800)]
    [InlineData(SeverityLevel.High, 2500)]
    [InlineData(SeverityLevel.Critical, 4000)]
    public void Each_severity_gives_the_expected_total_and_split(SeverityLevel severity, int expectedTotal)
    {
        var estimate = PotholeEstimator.Estimate(severity, DamageStage.Pothole, areaM2: null, depthCm: 5, TestData.Prices);

        Assert.Equal(expectedTotal, estimate.Total);
        Assert.Equal(expectedTotal * 0.36m, estimate.Materials);
        Assert.Equal(expectedTotal * 0.48m, estimate.Labor);
        Assert.Equal(expectedTotal * 0.16m, estimate.Equipment);
        Assert.Equal(estimate.Total, estimate.Materials + estimate.Labor + estimate.Equipment);
        Assert.Equal("TRY", estimate.Currency);
        Assert.Equal("Soğuk asfalt", estimate.Material.Name);
        Assert.Equal("m³", estimate.Material.Unit);
    }

    [Theory]
    [InlineData(SeverityLevel.Low, 1, 2, PriorityLevel.Routine, 0.5)]
    [InlineData(SeverityLevel.Medium, 2, 2, PriorityLevel.Medium, 1.0)]
    [InlineData(SeverityLevel.High, 3, 3, PriorityLevel.High, 1.5)]
    [InlineData(SeverityLevel.Critical, 5, 3, PriorityLevel.Critical, 2.0)]
    public void Hours_crew_priority_and_material_follow_the_severity(
        SeverityLevel severity, double hours, int crew, PriorityLevel priority, double asphaltM3)
    {
        var estimate = PotholeEstimator.Estimate(severity, DamageStage.Pothole, null, null, TestData.Prices);

        Assert.Equal(hours, estimate.LaborHours);
        Assert.Equal(crew, estimate.CrewSize);
        Assert.Equal(priority, estimate.SuggestedPriority);
        Assert.Equal(asphaltM3, estimate.Material.Quantity);
    }

    [Fact]
    public void It_uses_the_prices_passed_in()
    {
        var dearer = TestData.Prices with { Medium = 3000m };

        var estimate = PotholeEstimator.Estimate(SeverityLevel.Medium, DamageStage.Pothole, null, null, dearer);

        Assert.Equal(3000m, estimate.Total);
    }

    [Fact]
    public void Missing_area_still_gives_an_estimate()
    {
        var estimate = PotholeEstimator.Estimate(SeverityLevel.High, DamageStage.Unspecified, areaM2: null, depthCm: null, TestData.Prices);

        Assert.Equal(2500m, estimate.Total);
    }
}
