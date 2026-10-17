using Grpc.Core;
using SmartCity.VehicleCommunication.Application.Frames;
using Smartcity.Vehicle.V1;
using DomainRejectReason = SmartCity.VehicleCommunication.Domain.Frames.RejectReason;

namespace SmartCity.VehicleCommunication.Api.Grpc;

/// <summary>
/// Frames from the vehicles (V8–V12). Thin adapter: proto → command, call the use case, outcome → reply or
/// gRPC status. The reply table and what the Pi does for each one are in docs/design/vehicle/vehicle-protocol.md.
/// </summary>
public sealed class VehicleIngestGrpcService : VehicleIngestService.VehicleIngestServiceBase
{
    public const int MaxMessageBytes = 5 * 1024 * 1024;     // a 4 MB image plus its data
    private const string VehicleIdHeader = "x-vehicle-id";

    private readonly UploadFrame _uploadFrame;
    private readonly ILogger<VehicleIngestGrpcService> _logger;

    public VehicleIngestGrpcService(UploadFrame uploadFrame, ILogger<VehicleIngestGrpcService> logger)
    {
        _uploadFrame = uploadFrame;
        _logger = logger;
    }

    public override async Task<UploadFrameResponse> UploadFrame(UploadFrameRequest request, ServerCallContext context)
    {
        var vehicleId = VehicleIdOf(context)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, $"Missing the {VehicleIdHeader} header"));

        var command = new UploadFrameCommand(
            vehicleId,
            request.FrameId,
            FromEpochMilliseconds(request.CapturedAtUtc),
            request.Latitude,
            request.Longitude,
            request.GpsAccuracyM,
            request.GpsSatellites,
            request.HeadingDeg,
            request.SpeedKmh,
            request.Image.ToByteArray(),
            request.ContentType,
            request.WidthPx,
            request.HeightPx);

        UploadOutcome outcome;
        try
        {
            outcome = await _uploadFrame.HandleAsync(command, context.CancellationToken);
        }
        catch (Exception e) when (!context.CancellationToken.IsCancellationRequested)
        {
            // Database down or anything else unexpected (V16): the vehicle keeps the frame and tries later.
            _logger.LogError(e, "Upload of frame {FrameId} from vehicle {VehicleId} failed", request.FrameId, vehicleId);
            throw new RpcException(new Status(StatusCode.Unavailable, "The server cannot save frames right now"));
        }

        return outcome.Status switch
        {
            UploadStatus.Saved => new UploadFrameResponse { Result = UploadResult.Saved },
            UploadStatus.Duplicate => new UploadFrameResponse { Result = UploadResult.Duplicate },
            UploadStatus.Rejected => new UploadFrameResponse
            {
                Result = UploadResult.Rejected,
                RejectReason = ToProto(outcome.RejectReason!.Value)
            },
            UploadStatus.UnknownVehicle => throw new RpcException(new Status(StatusCode.Unauthenticated, "Unknown vehicle")),
            UploadStatus.NotAllowed => throw new RpcException(new Status(StatusCode.PermissionDenied, "The vehicle is suspended or retired")),
            UploadStatus.TooFast => throw new RpcException(new Status(StatusCode.ResourceExhausted, "Too many frames per second")),
            _ => throw new RpcException(new Status(StatusCode.Unavailable, "Frame storage is unavailable"))
        };
    }

    // SKELETON: the vehicle id comes from the "x-vehicle-id" header, so any caller can claim any vehicle. The real
    // version turns on mTLS on this endpoint (Kestrel ClientCertificateMode.RequireCertificate, on its own port
    // for the vehicles) and reads the id from the certificate here:
    // context.GetHttpContext().Connection.ClientCertificate?.GetNameInfo(X509NameType.SimpleName, false).
    private static string? VehicleIdOf(ServerCallContext context)
    {
        var value = context.RequestHeaders.GetValue(VehicleIdHeader);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // A value outside the DateTimeOffset range becomes MinValue, which FrameCheck rejects as BadTimestamp.
    private static DateTimeOffset FromEpochMilliseconds(long value) =>
        value is >= -62_135_596_800_000 and <= 253_402_300_799_999
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : DateTimeOffset.MinValue;

    private static RejectReason ToProto(DomainRejectReason reason) => reason switch
    {
        DomainRejectReason.OutsideServiceArea => RejectReason.OutsideServiceArea,
        DomainRejectReason.GpsInaccurate => RejectReason.GpsInaccurate,
        DomainRejectReason.ImageSize => RejectReason.ImageSize,
        DomainRejectReason.ImageType => RejectReason.ImageType,
        DomainRejectReason.BadTimestamp => RejectReason.BadTimestamp,
        DomainRejectReason.BadFrameId => RejectReason.BadFrameId,
        _ => RejectReason.Unspecified
    };
}
