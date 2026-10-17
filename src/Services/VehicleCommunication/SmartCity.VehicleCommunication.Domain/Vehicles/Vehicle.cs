using SmartCity.Contracts.Enums;

namespace SmartCity.VehicleCommunication.Domain.Vehicles;

/// <summary>
/// A municipality vehicle with a camera (aggregate root). One row in the <c>vehicles</c> table.
///
/// Two separate states:
/// <list type="bullet">
/// <item><see cref="Status"/> is set by the admin: Active, Suspended or Retired. Only Active vehicles may
/// upload frames. Retired is final: nothing can change a retired vehicle.</item>
/// <item><see cref="ConnectionStatus"/> is a fact: Online or Offline, from MQTT and the heartbeat.</item>
/// </list>
/// Every method that changes state increases <see cref="RowVersion"/> by one, so two writers on the
/// same vehicle at the same moment cannot both win.
/// </summary>
public sealed class Vehicle
{
    public const string IdPrefix = "MUN-VEH-";
    public const int MaxPlateLength = 20;
    public const int MaxNotesLength = 500;

    private Vehicle() { } // for EF

    public string Id { get; private set; } = default!;
    public string Plate { get; private set; } = default!;
    public int? DistrictId { get; private set; }
    public string Notes { get; private set; } = "";
    public VehicleStatus Status { get; private set; }

    public VehicleConnectionStatus ConnectionStatus { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public double? LastLatitude { get; private set; }
    public double? LastLongitude { get; private set; }
    public int PendingFrames { get; private set; }          // frames waiting on the vehicle's card

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long RowVersion { get; private set; }

    /// <summary>True for "MUN-VEH-" followed by one or more digits.</summary>
    public static bool IsValidId(string? id) =>
        id is not null
        && id.StartsWith(IdPrefix, StringComparison.Ordinal)
        && id.Length > IdPrefix.Length
        && id[IdPrefix.Length..].All(char.IsAsciiDigit);

    /// <summary>A new Active vehicle (V1). It is Offline until it connects.</summary>
    public static Vehicle Register(string id, string plate, int? districtId, string? notes, DateTimeOffset at)
    {
        if (!IsValidId(id))
            throw new DomainException($"The vehicle id must be {IdPrefix} followed by digits, e.g. {IdPrefix}001.");

        return new Vehicle
        {
            Id = id,
            Plate = CheckPlate(plate),
            DistrictId = districtId,
            Notes = CheckNotes(notes),
            Status = VehicleStatus.Active,
            ConnectionStatus = VehicleConnectionStatus.Offline,
            CreatedAt = at,
            UpdatedAt = at
        };
    }

    /// <summary>Change the plate, district or notes (V2). The id never changes.</summary>
    public void Update(string plate, int? districtId, string? notes, DateTimeOffset at)
    {
        ThrowIfRetired();
        Plate = CheckPlate(plate);
        DistrictId = districtId;
        Notes = CheckNotes(notes);
        Changed(at);
    }

    /// <summary>Stop accepting its frames (V3). Only an Active vehicle can be suspended.</summary>
    public void Suspend(DateTimeOffset at)
    {
        ThrowIfRetired();
        if (Status != VehicleStatus.Active)
            throw new DomainException($"Only an Active vehicle can be suspended; this one is {Status}.");
        Status = VehicleStatus.Suspended;
        Changed(at);
    }

    /// <summary>Accept its frames again (V3). Only a Suspended vehicle can be resumed.</summary>
    public void Resume(DateTimeOffset at)
    {
        ThrowIfRetired();
        if (Status != VehicleStatus.Suspended)
            throw new DomainException($"Only a Suspended vehicle can be resumed; this one is {Status}.");
        Status = VehicleStatus.Active;
        Changed(at);
    }

    /// <summary>Out of service for good (V4). Its old frames stay.</summary>
    public void Retire(DateTimeOffset at)
    {
        ThrowIfRetired();
        Status = VehicleStatus.Retired;
        Changed(at);
    }

    /// <summary>The vehicle said "online" on MQTT (V6).</summary>
    public void MarkOnline(DateTimeOffset at)
    {
        ConnectionStatus = VehicleConnectionStatus.Online;
        Changed(at);
    }

    /// <summary>The vehicle disappeared: its Last Will said "offline", or no heartbeat for too long (V18).</summary>
    public void MarkOffline(DateTimeOffset at)
    {
        ConnectionStatus = VehicleConnectionStatus.Offline;
        Changed(at);
    }

    /// <summary>A heartbeat (V7): where it is and how many frames wait on its card. A heartbeat means it is online.</summary>
    public void RecordHeartbeat(DateTimeOffset seenAt, double latitude, double longitude, int pendingFrames)
    {
        LastSeenAt = seenAt;
        LastLatitude = latitude;
        LastLongitude = longitude;
        PendingFrames = Math.Max(0, pendingFrames);
        ConnectionStatus = VehicleConnectionStatus.Online;
        Changed(seenAt);
    }

    private void ThrowIfRetired()
    {
        if (Status == VehicleStatus.Retired)
            throw new DomainException("The vehicle is Retired; it cannot be changed.");
    }

    private void Changed(DateTimeOffset at)
    {
        UpdatedAt = at;
        RowVersion++;
    }

    private static string CheckPlate(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
            throw new DomainException("The plate is required.");
        plate = plate.Trim();
        if (plate.Length > MaxPlateLength)
            throw new DomainException($"The plate is longer than {MaxPlateLength} characters.");
        return plate;
    }

    private static string CheckNotes(string? notes)
    {
        notes = notes?.Trim() ?? "";
        if (notes.Length > MaxNotesLength)
            throw new DomainException($"The notes are longer than {MaxNotesLength} characters.");
        return notes;
    }
}
