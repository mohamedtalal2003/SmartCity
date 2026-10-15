using SmartCity.Contracts.Enums;
using Xunit;

namespace SmartCity.Pothole.Domain.Tests;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>Verification passes (Pothole's part of P10).</summary>
public class VerificationTests
{
    private static VerificationPass Pass(VerificationResult result = VerificationResult.Gone) =>
        new(Guid.NewGuid(), result, Confidence: 0.8, "http://localhost:9000/frames-temp/MUN-VEH-002/after.jpg",
            new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Pass_on_an_open_pothole_adds_a_VerificationPass_observation_and_keeps_it_open()
    {
        var pothole = TestData.NewPothole();
        var pass = Pass(VerificationResult.StillThere);

        pothole.AddVerificationPass(pass);

        Assert.Equal(PotholeStatus.Open, pothole.Status);
        var observation = pothole.Observations[^1];
        Assert.Equal(ObservationKind.VerificationPass, observation.Kind);
        Assert.Equal("StillThere", observation.VerificationResult);
        Assert.Equal(pass.FrameId, observation.FrameId);
        Assert.Equal(pass.ImageUrl, observation.ImageUrl);
        Assert.Equal(pass.ObservedAt, observation.SeenAt);
        Assert.Equal(1, pothole.SightingsCount);
    }

    [Fact]
    public void Pass_after_closing_as_Repaired_is_still_saved()
    {
        var pothole = TestData.NewPothole();
        pothole.Close(ClosedReason.Repaired, null, DateTimeOffset.UtcNow);

        pothole.AddVerificationPass(Pass());

        Assert.Equal(2, pothole.Observations.Count);
        Assert.Equal(PotholeStatus.Closed, pothole.Status);
    }

    [Theory]
    [InlineData(ClosedReason.FalsePositive)]
    [InlineData(ClosedReason.ResolvedOtherwise)]
    public void Pass_on_a_pothole_closed_for_another_reason_is_ignored(ClosedReason reason)
    {
        var pothole = TestData.NewPothole();
        pothole.Close(reason, null, DateTimeOffset.UtcNow);
        var rowVersion = pothole.RowVersion;

        pothole.AddVerificationPass(Pass());

        Assert.Single(pothole.Observations);
        Assert.Equal(rowVersion, pothole.RowVersion);
    }
}
