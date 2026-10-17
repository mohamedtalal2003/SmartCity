using SmartCity.VehicleCommunication.Domain.Frames;
using SmartCity.VehicleCommunication.Domain.Vehicles;

namespace SmartCity.VehicleCommunication.Domain.Tests;

/// <summary>Builders shared by the tests.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    public static readonly FrameCheckOptions Options = new();

    public static Vehicle Vehicle(string id = "MUN-VEH-001") =>
        Domain.Vehicles.Vehicle.Register(id, "34 ABC 001", districtId: 7, notes: "garbage truck", Now);

    /// <summary>A frame that passes every check: Kadıköy, just taken, small JPEG, good GPS.</summary>
    public static FrameData GoodFrame() =>
        new(
            FrameId: Guid.NewGuid().ToString(),
            CapturedAt: Now.AddSeconds(-3),
            Latitude: 40.985,
            Longitude: 29.030,
            GpsAccuracyMeters: 2.5,
            ImageBytes: 200_000,
            ContentType: "image/jpeg");
}
