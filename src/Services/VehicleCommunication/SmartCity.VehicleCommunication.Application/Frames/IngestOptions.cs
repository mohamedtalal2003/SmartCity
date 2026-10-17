namespace SmartCity.VehicleCommunication.Application.Frames;

/// <summary>The "Ingest" config section.</summary>
public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>More frames than this in one second from one vehicle get RESOURCE_EXHAUSTED (V12).</summary>
    public int MaxFramesPerSecondPerVehicle { get; set; } = 5;
}
