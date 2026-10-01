namespace SmartCity.WorkOrder.Api.Data;

public class ProcessedIdempotencyKey
{
    public string Key { get; set; } = "";
    public bool Success { get; set; }
    public int ErrorCode { get; set; }
    public string Message { get; set; } = "";
    public string EntityId { get; set; } = "";
    public long ProcessedAtUtc { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
