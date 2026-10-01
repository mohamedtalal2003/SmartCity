using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SmartCity.Pothole.Api.Data;
using SmartCity.Pothole.Api.Domain;
using Smartcity.Queries.V1;
using Smartcity.Common.V1;
using Smartcity.Enums.V1;

namespace SmartCity.Pothole.Api.Grpc;

public sealed class RoadDamageQueryGrpcService : RoadDamageQueryService.RoadDamageQueryServiceBase
{
    private readonly PotholeDbContext _db;

    public RoadDamageQueryGrpcService(PotholeDbContext db) => _db = db;

    public override async Task<DamageDetailResponse> GetDamageById(GetDamageRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DamageId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid damage_id"));

        var p = await _db.Potholes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, context.CancellationToken);
        if (p is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Pothole not found"));

        return MapToDetail(p);
    }

    public override async Task<ListDamagesResponse> ListDamages(ListDamagesRequest request, ServerCallContext context)
    {
        var query = _db.Potholes.AsNoTracking().AsQueryable();

        if (request.Districts.Count > 0)
        {
            var districts = request.Districts
                .Select(d => (Contracts.Enums.District)(int)d)
                .ToHashSet();
            query = query.Where(p => districts.Contains(p.District));
        }

        if (request.MapArea is not null)
        {
            var sw = request.MapArea.SouthWest;
            var ne = request.MapArea.NorthEast;
            var envelope = new Envelope(sw.Longitude, ne.Longitude, sw.Latitude, ne.Latitude);
            var box = new GeometryFactory(new PrecisionModel(), 4326).ToGeometry(envelope);
            query = query.Where(p => p.Location.Intersects(box));
        }

        var pageSize = request.Pagination?.PageSize > 0 ? Math.Min(request.Pagination.PageSize, 100) : 20;
        var totalCount = await query.CountAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.Pagination?.PageToken) && Guid.TryParse(request.Pagination.PageToken, out var cursor))
            query = query.Where(p => p.Id.CompareTo(cursor) > 0);

        var records = await query
            .OrderBy(p => p.Id)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListDamagesResponse
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

    public override Task<DamageTimelineResponse> GetDamageTimeline(GetDamageRequest request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, "Not implemented in skeleton"));
    }

    private static DamageDetailResponse MapToDetail(PotholeRecord p) => new()
    {
        DamageId = p.Id.ToString(),
        Stage = (Smartcity.Enums.V1.DamageStage)(int)p.Stage,
        District = (Smartcity.Enums.V1.District)(int)p.District,
        Location = new GeoCoordinates { Latitude = p.Location.Y, Longitude = p.Location.X },
        RoadName = p.RoadName ?? "",
        LatestImageUrl = p.ImageUrls.Length > 0 ? p.ImageUrls[^1] : "",
        DetectionCount = p.DetectionCount,
        DetectionTimestampUtc = p.FirstSeenAt.ToUnixTimeMilliseconds(),
        LastSeenTimestampUtc = p.LastSeenAt.ToUnixTimeMilliseconds()
    };
}
