using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SmartCity.VehicleCommunication.Application.Connections;

namespace SmartCity.VehicleCommunication.Api.Mqtt;

/// <summary>
/// Reads the telemetry JSON (docs/design/vehicle/vehicle-protocol.md). capturedAt and location.latitude/longitude
/// are required; every other field may be missing. Fields this service does not use (moving, freeStorageMb,
/// softwareVersion) are not read.
/// </summary>
public static class TelemetryJson
{
    public static bool TryParse(string vehicleId, ArraySegment<byte> payload, [NotNullWhen(true)] out TelemetryMessage? message)
    {
        message = null;
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;

            if (!root.TryGetProperty("capturedAt", out var capturedAtEl)
                || !DateTimeOffset.TryParse(capturedAtEl.GetString(), out var capturedAt)
                || !root.TryGetProperty("location", out var location)
                || !location.TryGetProperty("latitude", out var latitude)
                || !location.TryGetProperty("longitude", out var longitude))
                return false;

            message = new TelemetryMessage(
                vehicleId,
                capturedAt,
                latitude.GetDouble(),
                longitude.GetDouble(),
                AccuracyMeters: Double(location, "accuracyMeters"),
                Heading: Double(location, "heading"),
                SpeedKmh: Double(root, "speedKmh") ?? 0,
                PendingFrames: root.TryGetProperty("pendingFrames", out var pending) ? pending.GetInt32() : 0,
                RouteId: root.TryGetProperty("routeId", out var route) ? route.GetString() : null,
                BatteryPercent: Double(root, "batteryPercent") ?? 0,
                CameraOnline: root.TryGetProperty("cameraOnline", out var camera) && camera.GetBoolean(),
                NetworkType: root.TryGetProperty("networkType", out var network) ? network.GetString() ?? "Unknown" : "Unknown",
                SignalStrengthDbm: root.TryGetProperty("signalStrengthDbm", out var signal) ? signal.GetInt32() : 0);
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static double? Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
