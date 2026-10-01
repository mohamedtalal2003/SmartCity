using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.WorkOrder.Api.Data;
using SmartCity.WorkOrder.Domain;
using Smartcity.Queries.V1;
using Smartcity.Common.V1;

namespace SmartCity.WorkOrder.Api.Grpc;

public sealed class WorkOrderQueryGrpcService : WorkOrderQueryService.WorkOrderQueryServiceBase
{
    private readonly WorkOrderDbContext _db;

    public WorkOrderQueryGrpcService(WorkOrderDbContext db) => _db = db;

    public override async Task<WorkOrderDetailResponse> GetWorkOrderById(
        GetWorkOrderRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.WorkOrderId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid work_order_id"));

        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, context.CancellationToken);

        if (wo is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Work order not found"));

        return MapToDetail(wo);
    }

    public override async Task<ListWorkOrdersResponse> ListWorkOrders(
        ListWorkOrdersRequest request, ServerCallContext context)
    {
        var query = _db.WorkOrders.AsNoTracking().AsQueryable();

        if (request.Statuses.Count > 0)
        {
            var statuses = request.Statuses
                .Select(s => (Contracts.Enums.WorkOrderStatus)(int)s)
                .ToHashSet();
            query = query.Where(w => statuses.Contains(w.Status));
        }

        if (request.Districts.Count > 0)
        {
            var districts = request.Districts
                .Select(d => (Contracts.Enums.District)(int)d)
                .ToHashSet();
            query = query.Where(w => districts.Contains(w.District));
        }

        if (request.OverdueOnly)
            query = query.Where(w => w.SlaDueAt < DateTimeOffset.UtcNow
                && w.Status != Contracts.Enums.WorkOrderStatus.Completed);

        var pageSize = request.Pagination?.PageSize > 0 ? Math.Min(request.Pagination.PageSize, 100) : 20;
        var totalCount = await query.CountAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.Pagination?.PageToken) &&
            Guid.TryParse(request.Pagination.PageToken, out var cursor))
            query = query.Where(w => w.Id.CompareTo(cursor) > 0);

        var records = await query
            .OrderBy(w => w.Id)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListWorkOrdersResponse
        {
            Pagination = new PaginationResponse
            {
                TotalCount = totalCount,
                NextPageToken = records.Count == pageSize ? records[^1].Id.ToString() : ""
            }
        };
        response.Records.AddRange(records.Select(MapToDetail));
        return response;
    }

    public override Task<ListWorkOrdersResponse> ListMyWorkOrders(
        ListMyWorkOrdersRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Not implemented in skeleton"));
    }

    private static WorkOrderDetailResponse MapToDetail(WorkOrderAggregate w) => new()
    {
        WorkOrderId = w.Id.ToString(),
        TicketNumber = w.TicketNumber,
        SourceDetectionId = w.SourceDetectionId.ToString(),
        SourceEntityType = (Smartcity.Enums.V1.SourceEntityType)(int)w.SourceEntityType,
        Status = (Smartcity.Enums.V1.WorkOrderStatus)(int)w.Status,
        Priority = (Smartcity.Enums.V1.PriorityLevel)(int)w.Priority,
        District = (Smartcity.Enums.V1.District)(int)w.District,
        Location = new GeoCoordinates
        {
            Latitude = w.Location.Latitude,
            Longitude = w.Location.Longitude
        },
        Title = w.Title,
        Description = w.Description,
        AssignedCrewId = w.AssignedCrewId ?? "",
        CreatedAtUtc = w.CreatedAt.ToUnixTimeMilliseconds(),
        SlaDueUtc = w.SlaDueAt?.ToUnixTimeMilliseconds() ?? 0,
        CompletedAtUtc = w.CompletedAt?.ToUnixTimeMilliseconds() ?? 0,
        AfterImageUrl = w.CompletionProofUrl ?? "",
        EstimatedCost = w.EstimatedCost.HasValue ? (double)w.EstimatedCost.Value : 0,
    };
}
