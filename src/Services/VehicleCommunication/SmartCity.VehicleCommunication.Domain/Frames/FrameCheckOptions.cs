namespace SmartCity.VehicleCommunication.Domain.Frames;

/// <summary>
/// The limits of <see cref="FrameCheck"/>. Bound from the "FrameCheck" config section; the defaults are
/// the starting numbers on the design page.
/// </summary>
public sealed class FrameCheckOptions
{
    public double MaxGpsAccuracyMeters { get; set; } = 25;
    public int MaxImageBytes { get; set; } = 4 * 1024 * 1024;
    public int MaxFutureMinutes { get; set; } = 5;

    /// <summary>A frame older than this is rejected. The Pi deletes unsent frames after the same time.</summary>
    public int FrameTtlHours { get; set; } = 24;

    // The Istanbul service area (a box around the city).
    public double MinLatitude { get; set; } = 40.80;
    public double MaxLatitude { get; set; } = 41.60;
    public double MinLongitude { get; set; } = 27.95;
    public double MaxLongitude { get; set; } = 29.98;
}
