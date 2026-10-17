namespace SmartCity.VehicleCommunication.Infrastructure.Storage;

/// <summary>The "FrameStorage" config section: where frames go and how hard to try (V13).</summary>
public sealed class FrameStorageOptions
{
    public const string SectionName = "FrameStorage";

    public string Bucket { get; set; } = "frames-temp";

    /// <summary>Retries inside one upload: 3 retries at 200, 400 and 800 ms.</summary>
    public int RetryCount { get; set; } = 3;
    public int RetryFirstDelayMs { get; set; } = 200;

    /// <summary>One storage call that takes longer than this counts as failed.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// The breaker opens when at least half of the uploads (min. 3) in the last 60 s failed, and stays open 30 s.
    /// One failed upload can take ~20 s (4 attempts with timeouts), so the window must hold several of them.
    /// </summary>
    public double BreakerFailureRatio { get; set; } = 0.5;
    public int BreakerMinimumThroughput { get; set; } = 3;
    public int BreakerSamplingSeconds { get; set; } = 60;
    public int BreakerBreakSeconds { get; set; } = 30;
}
