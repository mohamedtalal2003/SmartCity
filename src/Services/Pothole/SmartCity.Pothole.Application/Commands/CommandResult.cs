namespace SmartCity.Pothole.Application.Commands;

/// <summary>Why a command failed. The gRPC adapter turns it into the proto <c>ErrorCode</c>.</summary>
public enum CommandError
{
    None,
    NotFound,
    InvalidState,
    Validation
}

/// <summary>
/// What an officer command returns. A successful result is saved with its idempotency key, so the same
/// key sent again gets this same result back.
/// </summary>
public sealed record CommandResult(
    bool Success,
    CommandError Error,
    string Message,
    Guid? EntityId,
    bool Created,
    DateTimeOffset ProcessedAt)
{
    public static CommandResult Ok(Guid entityId, bool created, DateTimeOffset at) =>
        new(true, CommandError.None, "", entityId, created, at);

    public static CommandResult Failed(CommandError error, string message, DateTimeOffset at) =>
        new(false, error, message, null, false, at);
}

/// <summary>The command metadata every officer command carries (from the proto <c>CommandMetadata</c>).</summary>
public sealed record CommandContext(string IdempotencyKey, string SubmittedBy, Guid CorrelationId);
