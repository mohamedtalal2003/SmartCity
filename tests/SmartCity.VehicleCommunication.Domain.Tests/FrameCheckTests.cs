using SmartCity.VehicleCommunication.Domain.Frames;
using Xunit;

namespace SmartCity.VehicleCommunication.Domain.Tests;

/// <summary>Every reason a frame is rejected (V10).</summary>
public class FrameCheckTests
{
    private static RejectReason? Check(FrameData frame) => FrameCheck.Check(frame, TestData.Now, TestData.Options);

    [Fact]
    public void A_good_frame_passes()
    {
        Assert.Null(Check(TestData.GoodFrame()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("frame-1")]
    [InlineData("12345")]
    public void A_frame_id_that_is_not_a_uuid_is_BadFrameId(string frameId)
    {
        Assert.Equal(RejectReason.BadFrameId, Check(TestData.GoodFrame() with { FrameId = frameId }));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("")]
    public void A_frame_that_is_not_jpeg_is_ImageType(string contentType)
    {
        Assert.Equal(RejectReason.ImageType, Check(TestData.GoodFrame() with { ContentType = contentType }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4 * 1024 * 1024 + 1)]
    public void An_empty_or_too_large_image_is_ImageSize(int bytes)
    {
        Assert.Equal(RejectReason.ImageSize, Check(TestData.GoodFrame() with { ImageBytes = bytes }));
    }

    [Fact]
    public void An_image_of_exactly_the_limit_passes()
    {
        Assert.Null(Check(TestData.GoodFrame() with { ImageBytes = 4 * 1024 * 1024 }));
    }

    [Fact]
    public void A_frame_more_than_5_minutes_in_the_future_is_BadTimestamp()
    {
        var frame = TestData.GoodFrame() with { CapturedAt = TestData.Now.AddMinutes(6) };

        Assert.Equal(RejectReason.BadTimestamp, Check(frame));
    }

    [Fact]
    public void A_frame_a_little_in_the_future_passes_because_clocks_drift()
    {
        Assert.Null(Check(TestData.GoodFrame() with { CapturedAt = TestData.Now.AddMinutes(4) }));
    }

    [Fact]
    public void A_frame_older_than_the_ttl_is_BadTimestamp()
    {
        var frame = TestData.GoodFrame() with { CapturedAt = TestData.Now.AddHours(-25) };

        Assert.Equal(RejectReason.BadTimestamp, Check(frame));
    }

    [Fact]
    public void A_frame_from_yesterday_but_inside_the_ttl_passes()
    {
        Assert.Null(Check(TestData.GoodFrame() with { CapturedAt = TestData.Now.AddHours(-23) }));
    }

    [Theory]
    [InlineData(39.93, 32.85)]    // Ankara
    [InlineData(40.79, 29.00)]    // just south of the box
    [InlineData(41.61, 29.00)]    // just north
    [InlineData(41.00, 27.94)]    // just west
    [InlineData(41.00, 29.99)]    // just east
    public void A_frame_outside_Istanbul_is_OutsideServiceArea(double latitude, double longitude)
    {
        var frame = TestData.GoodFrame() with { Latitude = latitude, Longitude = longitude };

        Assert.Equal(RejectReason.OutsideServiceArea, Check(frame));
    }

    [Fact]
    public void A_frame_with_GPS_accuracy_worse_than_25_m_is_GpsInaccurate()
    {
        Assert.Equal(RejectReason.GpsInaccurate, Check(TestData.GoodFrame() with { GpsAccuracyMeters = 25.1 }));
    }

    [Fact]
    public void A_frame_with_GPS_accuracy_of_exactly_25_m_passes()
    {
        Assert.Null(Check(TestData.GoodFrame() with { GpsAccuracyMeters = 25 }));
    }

    [Fact]
    public void The_limits_come_from_the_options()
    {
        var strict = new FrameCheckOptions { MaxGpsAccuracyMeters = 5 };
        var frame = TestData.GoodFrame() with { GpsAccuracyMeters = 10 };

        Assert.Equal(RejectReason.GpsInaccurate, FrameCheck.Check(frame, TestData.Now, strict));
    }
}
