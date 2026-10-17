using SmartCity.VehicleCommunication.Application.Fleet;

namespace SmartCity.VehicleCommunication.Application.Abstractions;

/// <summary>
/// Server → vehicle messages (MQTT, retained). A vehicle that connects later gets the last message at once.
/// The JSON shapes are in docs/design/vehicle/vehicle-protocol.md.
/// </summary>
public interface IVehicleMessenger
{
    /// <summary>fleet/status: tells every vehicle to pause or run uploads (V13, V14).</summary>
    Task PublishFleetStatusAsync(FleetStatusMessage status, CancellationToken ct);

    /// <summary>vehicles/{id}/config: the capture settings of one vehicle (V19).</summary>
    Task PublishConfigAsync(string vehicleId, VehicleConfigMessage config, CancellationToken ct);
}
