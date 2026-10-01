using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.CostCalculation.Api.Data;
using SmartCity.CostCalculation.Api.Domain;
using Smartcity.Queries.V1;
using Smartcity.Common.V1;
using Smartcity.Enums.V1;

namespace SmartCity.CostCalculation.Api.Grpc;

public sealed class CostQueryGrpcService : CostQueryService.CostQueryServiceBase
{
    private readonly CostCalcDbContext _db;

    public CostQueryGrpcService(CostCalcDbContext db) => _db = db;

    public override async Task<EstimateDetailResponse> GetEstimateForEntity(GetEstimateRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SourceEntityId, out var entityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid source_entity_id"));

        var estimate = await _db.CostEstimates.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SourceEntityId == entityId, context.CancellationToken);

        if (estimate is null)
            throw new RpcException(new Status(StatusCode.NotFound, "No estimate found for entity"));

        return MapToDetail(estimate);
    }

    public override async Task<ListEstimatesResponse> ListEstimates(ListEstimatesRequest request, ServerCallContext context)
    {
        var query = _db.CostEstimates.AsNoTracking().AsQueryable();

        if (request.SourceTypes.Count > 0)
        {
            var types = request.SourceTypes
                .Select(t => (Contracts.Enums.SourceEntityType)(int)t)
                .ToHashSet();
            query = query.Where(e => types.Contains(e.SourceEntityType));
        }

        var pageSize = request.Pagination?.PageSize > 0 ? Math.Min(request.Pagination.PageSize, 100) : 20;
        var totalCount = await query.CountAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.Pagination?.PageToken) && Guid.TryParse(request.Pagination.PageToken, out var cursor))
            query = query.Where(e => e.Id.CompareTo(cursor) > 0);

        var records = await query
            .OrderBy(e => e.Id)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListEstimatesResponse
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

    private static EstimateDetailResponse MapToDetail(CostEstimate e) => new()
    {
        EstimateId = e.Id.ToString(),
        SourceEntityId = e.SourceEntityId.ToString(),
        SourceEntityType = (Smartcity.Enums.V1.SourceEntityType)(int)e.SourceEntityType,
        MaterialCost = (double)e.MaterialCost,
        LaborCost = (double)e.LaborCost,
        EquipmentCost = (double)e.EquipmentCost,
        TotalCost = (double)e.TotalEstimatedCost,
        Currency = e.Currency,
        EstimatedLaborHours = e.EstimatedLaborHours,
        SuggestedPriority = (Smartcity.Enums.V1.PriorityLevel)(int)e.SuggestedPriority,
        CalculatedAtUtc = e.CalculatedAt.ToUnixTimeMilliseconds()
    };
}
