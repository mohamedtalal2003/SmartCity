namespace SmartCity.Pothole.Application.Abstractions;

/// <summary>
/// The Istanbul district and neighborhood that contain a point, with their names. An id (and its name)
/// is null when no area was found (outside Istanbul or on the sea edge). Never 0 or a fake id.
/// </summary>
public sealed record AreaMatch(int? DistrictId, string? DistrictName, int? NeighborhoodId, string? NeighborhoodName);
