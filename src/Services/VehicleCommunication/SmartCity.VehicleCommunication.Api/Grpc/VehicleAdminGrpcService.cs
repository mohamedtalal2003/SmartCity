using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.VehicleCommunication.Application.Commands;
using SmartCity.VehicleCommunication.Application.Vehicles;
using Smartcity.Commands.V1;
using Smartcity.Common.V1;

namespace SmartCity.VehicleCommunication.Api.Grpc;

/// <summary>
/// Admin commands (V1–V4), called by the Gateway. Thin adapter: proto → command, call the use case, result →
/// <c>CommandResponse</c>. Errors come back like Pothole: success = false with an error code, never an exception.
/// </summary>
public sealed class VehicleAdminGrpcService : VehicleAdminService.VehicleAdminServiceBase
{
    private const int MaxIdempotencyKeyLength = 64; // the key column

    private readonly AddVehicle _addVehicle;
    private readonly UpdateVehicle _updateVehicle;
    private readonly ChangeVehicleStatus _changeStatus;

    public VehicleAdminGrpcService(AddVehicle addVehicle, UpdateVehicle updateVehicle, ChangeVehicleStatus changeStatus)
    {
        _addVehicle = addVehicle;
        _updateVehicle = updateVehicle;
        _changeStatus = changeStatus;
    }

    public override Task<CommandResponse> AddVehicle(AddVehicleRequest request, ServerCallContext context)
    {
        var command = new AddVehicleCommand(
            ToContext(request.Metadata),
            request.VehicleId.Trim(),
            request.Plate,
            request.HasDistrictId ? request.DistrictId : null,
            request.Notes);

        return RunAsync(() => _addVehicle.HandleAsync(command, context.CancellationToken), request.Metadata);
    }

    public override Task<CommandResponse> UpdateVehicle(UpdateVehicleRequest request, ServerCallContext context)
    {
        var command = new UpdateVehicleCommand(
            ToContext(request.Metadata),
            request.VehicleId.Trim(),
            request.HasPlate ? request.Plate : null,
            request.HasDistrictId ? request.DistrictId : null,
            request.HasNotes ? request.Notes : null);

        return RunAsync(() => _updateVehicle.HandleAsync(command, context.CancellationToken), request.Metadata);
    }

    public override Task<CommandResponse> SuspendVehicle(VehicleStatusRequest request, ServerCallContext context) =>
        ChangeStatus(request, VehicleStatusChange.Suspend, context);

    public override Task<CommandResponse> ResumeVehicle(VehicleStatusRequest request, ServerCallContext context) =>
        ChangeStatus(request, VehicleStatusChange.Resume, context);

    public override Task<CommandResponse> RetireVehicle(VehicleStatusRequest request, ServerCallContext context) =>
        ChangeStatus(request, VehicleStatusChange.Retire, context);

    private Task<CommandResponse> ChangeStatus(VehicleStatusRequest request, VehicleStatusChange change, ServerCallContext context)
    {
        var command = new ChangeVehicleStatusCommand(ToContext(request.Metadata), request.VehicleId.Trim(), change, request.Reason);
        return RunAsync(() => _changeStatus.HandleAsync(command, context.CancellationToken), request.Metadata);
    }

    private static async Task<CommandResponse> RunAsync(Func<Task<CommandResult>> useCase, CommandMetadata? metadata)
    {
        if (metadata?.IdempotencyKey.Length > MaxIdempotencyKeyLength)
            return ErrorResponse(CommandError.Validation, "The idempotency key is too long", metadata);

        try
        {
            return ToResponse(await useCase(), metadata);
        }
        catch (DbUpdateException)
        {
            // Someone else changed the vehicle at the same moment (row_version), or added the same id or key.
            return ErrorResponse(CommandError.InvalidState, "The vehicle was changed by someone else. Reload and try again", metadata);
        }
    }

    private static CommandContext ToContext(CommandMetadata? metadata) => new(
        metadata?.IdempotencyKey ?? "",
        string.IsNullOrWhiteSpace(metadata?.SubmittedBy) ? "unknown" : metadata.SubmittedBy,
        Guid.TryParse(metadata?.CorrelationId, out var correlationId) ? correlationId : Guid.NewGuid());

    private static CommandResponse ToResponse(CommandResult result, CommandMetadata? metadata) => new()
    {
        Success = result.Success,
        ErrorCode = ToErrorCode(result.Error),
        Message = result.Message,
        EntityId = result.EntityId ?? "",
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
