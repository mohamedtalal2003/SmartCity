namespace SmartCity.VehicleCommunication.Domain.Frames;

/// <summary>What <see cref="FrameCheck"/> needs to know about an uploaded frame.</summary>
public sealed record FrameData(
    string FrameId,
    DateTimeOffset CapturedAt,
    double Latitude,
    double Longitude,
    double GpsAccuracyMeters,
    int ImageBytes,
    string ContentType);

/// <summary>
/// The checks on an uploaded frame (V10). Pure: no I/O, the time is passed in.
/// Returns null when the frame is fine, otherwise the first reason it fails.
/// </summary>
public static class FrameCheck
{
    public const string JpegContentType = "image/jpeg";

    public static RejectReason? Check(FrameData frame, DateTimeOffset now, FrameCheckOptions options)
    {
        if (!Guid.TryParse(frame.FrameId, out _))
            return RejectReason.BadFrameId;

        if (!string.Equals(frame.ContentType, JpegContentType, StringComparison.OrdinalIgnoreCase))
            return RejectReason.ImageType;

        if (frame.ImageBytes <= 0 || frame.ImageBytes > options.MaxImageBytes)
            return RejectReason.ImageSize;

        if (frame.CapturedAt > now.AddMinutes(options.MaxFutureMinutes)
            || frame.CapturedAt < now.AddHours(-options.FrameTtlHours))
            return RejectReason.BadTimestamp;

        if (frame.Latitude < options.MinLatitude || frame.Latitude > options.MaxLatitude
            || frame.Longitude < options.MinLongitude || frame.Longitude > options.MaxLongitude)
            return RejectReason.OutsideServiceArea;

        if (frame.GpsAccuracyMeters > options.MaxGpsAccuracyMeters)
            return RejectReason.GpsInaccurate;

        return null;
    }
}
