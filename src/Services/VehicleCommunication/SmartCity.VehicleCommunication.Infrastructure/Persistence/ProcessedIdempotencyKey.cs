namespace SmartCity.VehicleCommunication.Infrastructure.Persistence;

/// <summary>
/// One admin command that already succeeded, kept so the same <c>Idempotency-Key</c> gets the same answer
/// (the same shape as in Pothole; the entity id is the vehicle id, a string). Only successful results are kept.
/// </summary>
public class ProcessedIdempotencyKey
{
    public string Key { get; set; } = "";
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = "";
    public string Message { get; set; } = "";
    public string? EntityId { get; set; }
    public bool Created { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
