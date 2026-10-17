using SmartCity.VehicleCommunication.Domain.Frames;

namespace SmartCity.VehicleCommunication.Application.Fleet;

/// <summary>
/// fleet/status: {"state":"running"} or {"state":"paused","reason":"storage","retryAfterSeconds":30}.
/// Null fields are left out of the JSON.
/// </summary>
public sealed record FleetStatusMessage(string State, string? Reason = null, int? RetryAfterSeconds = null)
{
    public static readonly FleetStatusMessage Running = new("running");

    public static FleetStatusMessage Paused(string reason, int retryAfterSeconds) => new("paused", reason, retryAfterSeconds);
}

/// <summary>vehicles/{id}/config: the capture settings. The JSON fields are in this order, camelCase.</summary>
public sealed record VehicleConfigMessage(
    int CaptureEveryMeters,
    int MaxSecondsBetweenFrames,
    bool SkipWhenStopped,
    int JpegQuality,
    string Resolution,
    int FrameTtlHours,
    int MaxUploadsPerSecond)
{
    /// <summary>frameTtlHours comes from the frame checks, so the vehicle and the server always use the same TTL.</summary>
    public static VehicleConfigMessage From(CaptureOptions capture, FrameCheckOptions checks) => new(
        capture.CaptureEveryMeters,
        capture.MaxSecondsBetweenFrames,
        capture.SkipWhenStopped,
        capture.JpegQuality,
        capture.Resolution,
        checks.FrameTtlHours,
        capture.MaxUploadsPerSecond);
}

/// <summary>
/// The "Capture" config section: the capture settings sent to every vehicle (values from the design page).
/// SKELETON: the same for every vehicle and only from appsettings. Later the admin changes them on a screen,
/// per vehicle, and the new config is published at once (V19).
/// </summary>
public sealed class CaptureOptions
{
    public const string SectionName = "Capture";

    public int CaptureEveryMeters { get; set; } = 10;
    public int MaxSecondsBetweenFrames { get; set; } = 10;
    public bool SkipWhenStopped { get; set; } = true;
    public int JpegQuality { get; set; } = 85;
    public string Resolution { get; set; } = "1920x1080";
    public int MaxUploadsPerSecond { get; set; } = 2;
}
