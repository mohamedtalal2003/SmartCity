namespace SmartCity.Contracts.Enums;

// Mirrors Protos/enums.proto (same names, same numbers).

/// <summary>Set by the admin. Only Active vehicles may upload frames. Retired is final.</summary>
public enum VehicleStatus
{
    Unspecified = 0,
    Active = 1,
    Suspended = 2,      // stopped by the admin; can be resumed
    Retired = 3         // out of service for good
}

/// <summary>Whether the vehicle is connected now (MQTT online/offline + heartbeat).</summary>
public enum VehicleConnectionStatus
{
    Unspecified = 0,
    Online = 1,
    Offline = 2
}
