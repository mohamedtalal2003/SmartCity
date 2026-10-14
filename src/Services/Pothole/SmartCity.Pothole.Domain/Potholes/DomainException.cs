namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>Thrown when a pothole rule is broken. Nothing is changed when it is thrown.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
