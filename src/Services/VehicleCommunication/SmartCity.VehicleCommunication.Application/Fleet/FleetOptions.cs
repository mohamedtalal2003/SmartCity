namespace SmartCity.VehicleCommunication.Application.Fleet;

/// <summary>The "Fleet" config section: heartbeat, offline and storage-pause timings (starting numbers from the design page).</summary>
public sealed class FleetOptions
{
    public const string SectionName = "Fleet";

    /// <summary>Write a vehicle's heartbeat (last seen, location, pending frames) to the table at most this often (V7).</summary>
    public int HeartbeatWriteSeconds { get; set; } = 30;

    /// <summary>An Online vehicle with no heartbeat for this long is marked Offline (V18).</summary>
    public int OfflineAfterSeconds { get; set; } = 60;
    public int OfflineSweepSeconds { get; set; } = 30;

    /// <summary>While uploads are paused, check the storage this often (V14).</summary>
    public int StorageCheckSeconds { get; set; } = 10;

    /// <summary>The hint sent to the vehicles in the "paused" message.</summary>
    public int RetryAfterSeconds { get; set; } = 30;
}
