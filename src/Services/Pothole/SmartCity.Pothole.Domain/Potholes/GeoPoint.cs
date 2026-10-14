namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>A place on the map in WGS84 degrees.</summary>
public sealed record GeoPoint(double Latitude, double Longitude);
