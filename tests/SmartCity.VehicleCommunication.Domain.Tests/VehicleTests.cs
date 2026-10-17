using SmartCity.Contracts.Enums;
using SmartCity.VehicleCommunication.Domain.Vehicles;
using Xunit;

namespace SmartCity.VehicleCommunication.Domain.Tests;

/// <summary>Admin commands (V1–V4) and connection state (V6, V7, V18).</summary>
public class VehicleTests
{
    [Fact]
    public void Register_makes_an_Active_Offline_vehicle()
    {
        var vehicle = TestData.Vehicle();

        Assert.Equal("MUN-VEH-001", vehicle.Id);
        Assert.Equal("34 ABC 001", vehicle.Plate);
        Assert.Equal(7, vehicle.DistrictId);
        Assert.Equal(VehicleStatus.Active, vehicle.Status);
        Assert.Equal(VehicleConnectionStatus.Offline, vehicle.ConnectionStatus);
        Assert.Null(vehicle.LastSeenAt);
        Assert.Equal(TestData.Now, vehicle.CreatedAt);
    }

    [Theory]
    [InlineData("MUN-VEH-001")]
    [InlineData("MUN-VEH-1")]
    [InlineData("MUN-VEH-12345")]
    public void A_valid_id_is_MUN_VEH_and_digits(string id)
    {
        Assert.True(Vehicle.IsValidId(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MUN-VEH-")]
    [InlineData("MUN-VEH-00A")]
    [InlineData("mun-veh-001")]
    [InlineData("BUS-001")]
    public void Register_with_a_bad_id_is_rejected(string id)
    {
        Assert.False(Vehicle.IsValidId(id));
        Assert.Throws<DomainException>(() => Vehicle.Register(id, "34 ABC 001", null, null, TestData.Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Register_without_a_plate_is_rejected(string? plate)
    {
        Assert.Throws<DomainException>(() => Vehicle.Register("MUN-VEH-001", plate!, null, null, TestData.Now));
    }

    [Fact]
    public void Update_changes_the_plate_district_and_notes()
    {
        var vehicle = TestData.Vehicle();
        var later = TestData.Now.AddHours(1);

        vehicle.Update("34 XYZ 999", districtId: null, notes: "moved", later);

        Assert.Equal("34 XYZ 999", vehicle.Plate);
        Assert.Null(vehicle.DistrictId);
        Assert.Equal("moved", vehicle.Notes);
        Assert.Equal(later, vehicle.UpdatedAt);
        Assert.Equal(1, vehicle.RowVersion);
    }

    [Fact]
    public void Suspend_then_Resume_goes_back_to_Active()
    {
        var vehicle = TestData.Vehicle();

        vehicle.Suspend(TestData.Now);
        Assert.Equal(VehicleStatus.Suspended, vehicle.Status);

        vehicle.Resume(TestData.Now);
        Assert.Equal(VehicleStatus.Active, vehicle.Status);
        Assert.Equal(2, vehicle.RowVersion);
    }

    [Fact]
    public void Suspend_a_Suspended_vehicle_is_rejected()
    {
        var vehicle = TestData.Vehicle();
        vehicle.Suspend(TestData.Now);

        Assert.Throws<DomainException>(() => vehicle.Suspend(TestData.Now));
    }

    [Fact]
    public void Resume_an_Active_vehicle_is_rejected()
    {
        var vehicle = TestData.Vehicle();

        Assert.Throws<DomainException>(() => vehicle.Resume(TestData.Now));
    }

    [Fact]
    public void Retire_works_from_Active_and_from_Suspended()
    {
        var active = TestData.Vehicle("MUN-VEH-001");
        var suspended = TestData.Vehicle("MUN-VEH-002");
        suspended.Suspend(TestData.Now);

        active.Retire(TestData.Now);
        suspended.Retire(TestData.Now);

        Assert.Equal(VehicleStatus.Retired, active.Status);
        Assert.Equal(VehicleStatus.Retired, suspended.Status);
    }

    [Fact]
    public void Retired_is_final()
    {
        var vehicle = TestData.Vehicle();
        vehicle.Retire(TestData.Now);

        Assert.Throws<DomainException>(() => vehicle.Resume(TestData.Now));
        Assert.Throws<DomainException>(() => vehicle.Suspend(TestData.Now));
        Assert.Throws<DomainException>(() => vehicle.Retire(TestData.Now));
        Assert.Throws<DomainException>(() => vehicle.Update("34 XYZ 999", null, null, TestData.Now));
        Assert.Equal(VehicleStatus.Retired, vehicle.Status);
    }

    [Fact]
    public void MarkOnline_and_MarkOffline_change_only_the_connection()
    {
        var vehicle = TestData.Vehicle();
        vehicle.Suspend(TestData.Now);

        vehicle.MarkOnline(TestData.Now);
        Assert.Equal(VehicleConnectionStatus.Online, vehicle.ConnectionStatus);

        vehicle.MarkOffline(TestData.Now);
        Assert.Equal(VehicleConnectionStatus.Offline, vehicle.ConnectionStatus);
        Assert.Equal(VehicleStatus.Suspended, vehicle.Status);
    }

    [Fact]
    public void RecordHeartbeat_keeps_the_last_location_and_pending_frames_and_marks_Online()
    {
        var vehicle = TestData.Vehicle();
        var seenAt = TestData.Now.AddMinutes(5);

        vehicle.RecordHeartbeat(seenAt, 40.985, 29.030, pendingFrames: 12);

        Assert.Equal(seenAt, vehicle.LastSeenAt);
        Assert.Equal(40.985, vehicle.LastLatitude);
        Assert.Equal(29.030, vehicle.LastLongitude);
        Assert.Equal(12, vehicle.PendingFrames);
        Assert.Equal(VehicleConnectionStatus.Online, vehicle.ConnectionStatus);
    }
}
