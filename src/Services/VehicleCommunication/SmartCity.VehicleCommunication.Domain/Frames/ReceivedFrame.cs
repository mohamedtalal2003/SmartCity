namespace SmartCity.VehicleCommunication.Domain.Frames;

/// <summary>
/// One frame that was saved (a row in <c>received_frames</c>). The frame id is the primary key, so the same
/// frame can never be saved twice (V9). It also says which vehicle sent each frame and when.
/// </summary>
public sealed class ReceivedFrame
{
    private ReceivedFrame() { } // for EF

    public ReceivedFrame(Guid frameId, string vehicleId, DateTimeOffset capturedAt, DateTimeOffset receivedAt, string storageKey, int sizeBytes)
    {
        FrameId = frameId;
        VehicleId = vehicleId;
        CapturedAt = capturedAt;
        ReceivedAt = receivedAt;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
    }

    public Guid FrameId { get; private set; }
    public string VehicleId { get; private set; } = default!;
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string StorageKey { get; private set; } = default!;
    public int SizeBytes { get; private set; }
}
