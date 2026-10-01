using Grpc.Core;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;
using SmartCity.WorkOrder.Api.Data;
using SmartCity.WorkOrder.Domain;
using Smartcity.Commands.V1;
using Smartcity.Common.V1;

namespace SmartCity.WorkOrder.Api.Grpc;

public sealed class WorkOrderCommandGrpcService : WorkOrderCommandService.WorkOrderCommandServiceBase
{
    private readonly WorkOrderDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;

    public WorkOrderCommandGrpcService(WorkOrderDbContext db, IPublishEndpoint publishEndpoint)
    {
        _db = db;
        _publishEndpoint = publishEndpoint;
    }

    public override async Task<CommandResponse> AssignCrew(AssignCrewRequest request, ServerCallContext context)
    {
        var (wo, idemResult) = await LoadAndCheckIdempotency(request.WorkOrderId, request.Metadata, context);
        if (idemResult is not null) return idemResult;

        try
        {
            wo!.AssignCrew(request.CrewId, request.Metadata.SubmittedBy);
        }
        catch (InvalidStateTransitionException)
        {
            return ErrorResponse(ErrorCode.InvalidState, "Cannot assign crew in current state", request.Metadata);
        }

        return await PublishAndSaveWithIdempotency(wo, request.Metadata, context.CancellationToken);
    }

    public override async Task<CommandResponse> MarkEnRoute(WorkOrderActionRequest request, ServerCallContext context)
        => await SimpleTransition(request.WorkOrderId, request.Metadata, (wo, by) => wo.MarkEnRoute(by), context);

    public override async Task<CommandResponse> MarkOnSite(WorkOrderActionRequest request, ServerCallContext context)
        => await SimpleTransition(request.WorkOrderId, request.Metadata, (wo, by) => wo.MarkOnSite(by), context);

    public override async Task<CommandResponse> ResumeWork(WorkOrderActionRequest request, ServerCallContext context)
        => await SimpleTransition(request.WorkOrderId, request.Metadata, (wo, by) => wo.Resume(by), context);

    public override async Task<CommandResponse> PauseWork(PauseWorkRequest request, ServerCallContext context)
    {
        var (wo, idemResult) = await LoadAndCheckIdempotency(request.WorkOrderId, request.Metadata, context);
        if (idemResult is not null) return idemResult;

        var reason = (PauseReason)(int)request.Reason;
        try { wo!.Pause(reason, request.Metadata.SubmittedBy); }
        catch (InvalidStateTransitionException)
        {
            return ErrorResponse(ErrorCode.InvalidState, "Cannot pause in current state", request.Metadata);
        }

        return await PublishAndSaveWithIdempotency(wo, request.Metadata, context.CancellationToken);
    }

    public override async Task<CommandResponse> SubmitCompletionProof(
        SubmitCompletionProofRequest request, ServerCallContext context)
    {
        var (wo, idemResult) = await LoadAndCheckIdempotency(request.WorkOrderId, request.Metadata, context);
        if (idemResult is not null) return idemResult;

        try { wo!.Complete(request.AfterImageUrl, request.Metadata.SubmittedBy); }
        catch (InvalidStateTransitionException)
        {
            return ErrorResponse(ErrorCode.InvalidState, "Cannot complete in current state", request.Metadata);
        }

        return await PublishAndSaveWithIdempotency(wo, request.Metadata, context.CancellationToken);
    }

    public override Task<CommandResponse> CreateWorkOrder(CreateWorkOrderRequest request, ServerCallContext context)
        => Task.FromResult(ErrorResponse(ErrorCode.Unspecified, "Not implemented in skeleton", request.Metadata));

    public override Task<CommandResponse> ReassignWorkOrder(ReassignWorkOrderRequest request, ServerCallContext context)
        => Task.FromResult(ErrorResponse(ErrorCode.Unspecified, "Not implemented in skeleton", request.Metadata));

    public override Task<CommandResponse> ChangePriority(ChangePriorityRequest request, ServerCallContext context)
        => Task.FromResult(ErrorResponse(ErrorCode.Unspecified, "Not implemented in skeleton", request.Metadata));

    public override Task<CommandResponse> RejectResolution(RejectResolutionRequest request, ServerCallContext context)
        => Task.FromResult(ErrorResponse(ErrorCode.Unspecified, "Not implemented in skeleton", request.Metadata));

    public override Task<CommandResponse> LogWorkDetails(LogWorkDetailsRequest request, ServerCallContext context)
        => Task.FromResult(ErrorResponse(ErrorCode.Unspecified, "Not implemented in skeleton", request.Metadata));

    private async Task<CommandResponse> SimpleTransition(
        string workOrderId, CommandMetadata metadata,
        Action<WorkOrderAggregate, string> action, ServerCallContext context)
    {
        var (wo, idemResult) = await LoadAndCheckIdempotency(workOrderId, metadata, context);
        if (idemResult is not null) return idemResult;

        try { action(wo!, metadata.SubmittedBy); }
        catch (InvalidStateTransitionException)
        {
            return ErrorResponse(ErrorCode.InvalidState, "Invalid state transition", metadata);
        }

        return await PublishAndSaveWithIdempotency(wo!, metadata, context.CancellationToken);
    }

    private async Task<(WorkOrderAggregate?, CommandResponse?)> LoadAndCheckIdempotency(
        string workOrderId, CommandMetadata metadata, ServerCallContext context)
    {
        if (!Guid.TryParse(workOrderId, out var id))
            return (null, ErrorResponse(ErrorCode.Validation, "Invalid work_order_id", metadata));

        if (!string.IsNullOrEmpty(metadata.IdempotencyKey))
        {
            var existing = await _db.ProcessedIdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Key == metadata.IdempotencyKey, context.CancellationToken);

            if (existing is not null)
            {
                return (null, new CommandResponse
                {
                    Success = existing.Success,
                    ErrorCode = (Smartcity.Common.V1.ErrorCode)existing.ErrorCode,
                    Message = existing.Message,
                    EntityId = existing.EntityId,
                    RequestId = metadata.IdempotencyKey,
                    ProcessedAtUtc = existing.ProcessedAtUtc,
                });
            }
        }

        var wo = await _db.WorkOrders
            .Include(w => w.StatusHistory)
            .FirstOrDefaultAsync(w => w.Id == id, context.CancellationToken);

        if (wo is null)
            return (null, ErrorResponse(ErrorCode.NotFound, "Work order not found", metadata));

        return (wo, null);
    }

    private async Task<CommandResponse> PublishAndSaveWithIdempotency(
        WorkOrderAggregate wo, CommandMetadata metadata, CancellationToken ct)
    {
        var response = SuccessResponse(wo.Id, metadata);

        if (!string.IsNullOrEmpty(metadata.IdempotencyKey))
        {
            _db.ProcessedIdempotencyKeys.Add(new ProcessedIdempotencyKey
            {
                Key = metadata.IdempotencyKey,
                Success = response.Success,
                ErrorCode = (int)response.ErrorCode,
                Message = response.Message,
                EntityId = response.EntityId,
                ProcessedAtUtc = response.ProcessedAtUtc,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await PublishDomainEventsAndSave(wo, ct);
        return response;
    }

    private async Task PublishDomainEventsAndSave(WorkOrderAggregate wo, CancellationToken ct)
    {
        foreach (var evt in wo.DomainEvents)
        {
            if (evt is WorkOrderStatusChangedDomainEvent changed)
            {
                var e = new WorkOrderStatusChanged
                {
                    CorrelationId = wo.CorrelationId,
                    WorkOrderId = wo.Id,
                    TicketNumber = wo.TicketNumber,
                    SourceDetectionId = wo.SourceDetectionId,
                    SourceEntityId = wo.SourceEntityId,
                    SourceEntityType = wo.SourceEntityType,
                    PreviousStatus = changed.PreviousStatus,
                    NewStatus = wo.Status,
                    ChangedBy = changed.HistoryEntry.ChangedBy,
                    Priority = wo.Priority,
                    AssignedCrewId = wo.AssignedCrewId,
                    PauseReason = wo.CurrentPauseReason,
                    TimeInPreviousStatus = changed.HistoryEntry.ChangedAt - wo.CreatedAt,
                    SlaBreached = wo.SlaDueAt.HasValue && DateTimeOffset.UtcNow > wo.SlaDueAt.Value,
                };
                await _publishEndpoint.PublishEvent(e, ct);
            }
        }

        wo.ClearDomainEvents();

        foreach (var entry in wo.StatusHistory)
        {
            var tracked = _db.ChangeTracker.Entries<StatusHistoryEntry>()
                .FirstOrDefault(e => e.Entity.Id == entry.Id);
            if (tracked is null)
                _db.Entry(entry).State = EntityState.Added;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static CommandResponse SuccessResponse(Guid entityId, CommandMetadata metadata) => new()
    {
        Success = true,
        ErrorCode = Smartcity.Common.V1.ErrorCode.None,
        EntityId = entityId.ToString(),
        RequestId = metadata.IdempotencyKey,
        ProcessedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };

    private static CommandResponse ErrorResponse(ErrorCode code, string message, CommandMetadata metadata) => new()
    {
        Success = false,
        ErrorCode = (Smartcity.Common.V1.ErrorCode)(int)code,
        Message = message,
        RequestId = metadata?.IdempotencyKey ?? "",
        ProcessedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };
}
