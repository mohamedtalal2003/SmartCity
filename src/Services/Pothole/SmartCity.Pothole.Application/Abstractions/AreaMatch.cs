namespace SmartCity.Pothole.Application.Abstractions;

/// <summary>
/// The Istanbul district and neighborhood that contain a point. An id is null when no area was found
/// (outside Istanbul or on the sea edge). Never 0 or a fake id.
/// </summary>
public sealed record AreaMatch(int? DistrictId, int? NeighborhoodId);
