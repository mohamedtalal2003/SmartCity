using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.Pothole.Application.Commands;
using SmartCity.Pothole.Application.Officers;
using Smartcity.Commands.V1;
using Smartcity.Common.V1;

namespace SmartCity.Pothole.Api.Grpc;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Officer commands (P1, P2). Thin adapter: proto → command, call the use case, result → <c>CommandResponse</c>.
/// Errors come back the way Work Order does it: success = false with an error code, never an exception.
/// </summary>
public sealed class PotholeCommandGrpcService : PotholeCommandService.PotholeCommandServiceBase
{
    private const int MaxIdempotencyKeyLength = 64; // the key column

    private readonly AddPotholeByOfficer _addPothole;
    private readonly CorrectPothole _correctPothole;

    public PotholeCommandGrpcService(AddPotholeByOfficer addPothole, CorrectPothole correctPothole)
    {
        _addPothole = addPothole;
        _correctPothole = correctPothole;
    }

    public override async Task<CommandResponse> AddPothole(AddPotholeRequest request, ServerCallContext context)
    {
        if (request.Metadata?.IdempotencyKey.Length > MaxIdempotencyKeyLength)
            return ErrorResponse(CommandError.Validation, "The idempotency key is too long", request.Metadata);
        if (request.Location is null)
            return ErrorResponse(CommandError.Validation, "The location is required", request.Metadata);

        var command = new AddPotholeCommand(
            ToContext(request.Metadata),
            request.Location.Latitude,
            request.Location.Longitude,
            request.ImageUrl,
            request.HasSeverity ? (Contracts.Enums.SeverityLevel)(int)request.Severity : null,
            // 0 = not given (proto). The depth stays in cm.
            request.AreaM2 > 0 ? request.AreaM2 : null,
            request.DepthCm > 0 ? request.DepthCm : null,
            ToSource(request.Source),
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes);

        return await RunAsync(() => _addPothole.HandleAsync(command, context.CancellationToken), request.Metadata);
    }

    public override async Task<CommandResponse> CorrectPothole(CorrectPotholeRequest request, ServerCallContext context)
    {
        if (request.Metadata?.IdempotencyKey.Length > MaxIdempotencyKeyLength)
            return ErrorResponse(CommandError.Validation, "The idempotency key is too long", request.Metadata);
        if (!Guid.TryParse(request.PotholeId, out var potholeId))
            return ErrorResponse(CommandError.Validation, "Invalid pothole_id", request.Metadata);

        var command = new CorrectPotholeCommand(
            ToContext(request.Metadata),
            potholeId,
            request.HasSeverity ? (Contracts.Enums.SeverityLevel)(int)request.Severity : null,
            request.HasStage ? (Contracts.Enums.DamageStage)(int)request.Stage : null,
            request.HasAreaM2 ? request.AreaM2 : null,
            request.HasDepthCm ? request.DepthCm : null,
            request.Reason);

        return await RunAsync(() => _correctPothole.HandleAsync(command, context.CancellationToken), request.Metadata);
    }

    private static async Task<CommandResponse> RunAsync(Func<Task<CommandResult>> useCase, CommandMetadata? metadata)
    {
        try
        {
            return ToResponse(await useCase(), metadata);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed the pothole at the same moment (row_version). The screen reloads and retries.
            return ErrorResponse(CommandError.InvalidState, "The pothole was changed by someone else. Reload and try again", metadata);
        }
    }

    private static CommandContext ToContext(CommandMetadata? metadata) => new(
        metadata?.IdempotencyKey ?? "",
        string.IsNullOrWhiteSpace(metadata?.SubmittedBy) ? "unknown" : metadata.SubmittedBy,
        Guid.TryParse(metadata?.CorrelationId, out var correlationId) ? correlationId : Guid.NewGuid());

    // Not set means an officer entered it.
    private static PotholeSource ToSource(Smartcity.Enums.V1.PotholeSource source) => source switch
    {
        Smartcity.Enums.V1.PotholeSource.CitizenReport => PotholeSource.CitizenReport,
        Smartcity.Enums.V1.PotholeSource.Ai => PotholeSource.Ai,
        _ => PotholeSource.Officer
    };

    private static CommandResponse ToResponse(CommandResult result, CommandMetadata? metadata) => new()
    {
        Success = result.Success,
        ErrorCode = ToErrorCode(result.Error),
        Message = result.Message,
        EntityId = result.EntityId?.ToString() ?? "",
        RequestId = metadata?.IdempotencyKey ?? "",
        ProcessedAtUtc = result.ProcessedAt.ToUnixTimeMilliseconds(),
        Created = result.Created
    };

    private static CommandResponse ErrorResponse(CommandError error, string message, CommandMetadata? metadata) =>
        ToResponse(CommandResult.Failed(error, message, DateTimeOffset.UtcNow), metadata);

    private static ErrorCode ToErrorCode(CommandError error) => error switch
    {
        CommandError.None => ErrorCode.None,
        CommandError.NotFound => ErrorCode.NotFound,
        CommandError.InvalidState => ErrorCode.InvalidState,
        CommandError.Validation => ErrorCode.Validation,
        _ => ErrorCode.Internal
    };
}
