namespace SmartCity.VehicleCommunication.Domain.Vehicles;

/// <summary>Thrown when a vehicle rule is broken. Nothing is changed when it is thrown.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
