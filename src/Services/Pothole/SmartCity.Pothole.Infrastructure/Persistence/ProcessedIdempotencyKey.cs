namespace SmartCity.Pothole.Infrastructure.Persistence;

/// <summary>
/// One officer command that already succeeded, kept so the same <c>Idempotency-Key</c> gets the same
/// answer (the same shape as in Work Order, plus <see cref="Created"/>). Only successful results are kept.
/// </summary>
public class ProcessedIdempotencyKey
{
    public string Key { get; set; } = "";
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = "";
    public string Message { get; set; } = "";
    public Guid? EntityId { get; set; }
    public bool Created { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
