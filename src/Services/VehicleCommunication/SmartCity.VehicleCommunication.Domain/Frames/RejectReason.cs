namespace SmartCity.VehicleCommunication.Domain.Frames;

/// <summary>Why a frame is rejected for good (V10). Same names as <c>RejectReason</c> in vehicle.proto.</summary>
public enum RejectReason
{
    OutsideServiceArea,     // the location is outside Istanbul
    GpsInaccurate,          // GPS accuracy worse than the limit
    ImageSize,              // empty, or larger than the limit
    ImageType,              // not image/jpeg
    BadTimestamp,           // too far in the future, or older than the frame TTL
    BadFrameId              // frame_id is not a UUID
}
