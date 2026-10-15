using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.Pothole.Infrastructure.Persistence;
using Smartcity.Common.V1;
using Smartcity.Queries.V1;

namespace SmartCity.Pothole.Api.Grpc;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Read side. Queries <see cref="PotholeDbContext"/> directly with AsNoTracking (no repository for
/// reads) and maps to the proto messages.
/// </summary>
public sealed class RoadDamageQueryGrpcService : RoadDamageQueryService.RoadDamageQueryServiceBase
{
    private const int MaxTimelineEntries = 200;

    private readonly PotholeDbContext _db;

    public RoadDamageQueryGrpcService(PotholeDbContext db) => _db = db;

    public override async Task<DamageDetailResponse> GetDamageById(GetDamageRequest request, ServerCallContext context)
    {
        var id = ParseId(request.DamageId);

        var row = await ToRows(_db.Potholes.AsNoTracking().Where(p => p.Id == id))
            .FirstOrDefaultAsync(context.CancellationToken);
        if (row is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Pothole not found"));

        return ToResponse(row);
    }

    public override async Task<ListDamagesResponse> ListDamages(ListDamagesRequest request, ServerCallContext context)
    {
        IQueryable<Pothole> query = _db.Potholes.AsNoTracking();

        // SKELETON: district_ids, neighborhood_ids and the other new filters are not applied yet. They come
        // with the pothole list PR (P12).

        if (request.MapArea is not null)
        {
            var sw = request.MapArea.SouthWest;
            var ne = request.MapArea.NorthEast;
            query = _db.Potholes
                .FromSqlInterpolated($"""
                    SELECT * FROM potholes
                    WHERE ST_Intersects(location, ST_MakeEnvelope({sw.Longitude}, {sw.Latitude}, {ne.Longitude}, {ne.Latitude}, 4326)::geography)
                    """)
                .AsNoTracking();
        }

        var pageSize = request.Pagination?.PageSize > 0 ? Math.Min(request.Pagination.PageSize, 100) : 20;
        var totalCount = await query.CountAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.Pagination?.PageToken) && Guid.TryParse(request.Pagination.PageToken, out var cursor))
            query = query.Where(p => p.Id.CompareTo(cursor) > 0);

        var rows = await ToRows(query.OrderBy(p => p.Id).Take(pageSize)).ToListAsync(context.CancellationToken);

        var response = new ListDamagesResponse
        {
            Pagination = new PaginationResponse
            {
                TotalCount = totalCount,
                NextPageToken = rows.Count == pageSize ? rows[^1].Id.ToString() : ""
            }
        };
        response.Records.AddRange(rows.Select(ToResponse));
        return response;
    }

    public override async Task<DamageTimelineResponse> GetDamageTimeline(GetDamageTimelineRequest request, ServerCallContext context)
    {
        var id = ParseId(request.DamageId);
        var ct = context.CancellationToken;

        if (!await _db.Potholes.AnyAsync(p => p.Id == id, ct))
            throw new RpcException(new Status(StatusCode.NotFound, "Pothole not found"));

        // The first observation is when the pothole was detected; every later one is a re-detection.
        var firstObservationId = await _db.Observations
            .Where(o => o.PotholeId == id)
            .OrderBy(o => o.SeenAt)
            .Select(o => o.Id)
            .FirstAsync(ct);

        // SKELETON: request.pagination is not used yet, so only the newest 200 observations are returned,
        // and the new entry fields (damage_type, severity, stage, kind) are not filled. Paging, ticket and
        // closing entries come with the timeline PR (P14).
        var observations = await _db.Observations
            .AsNoTracking()
            .Where(o => o.PotholeId == id)
            .OrderByDescending(o => o.SeenAt)
            .Take(MaxTimelineEntries)
            .ToListAsync(ct);

        var response = new DamageTimelineResponse { DamageId = id.ToString() };
        response.Entries.AddRange(observations.Select(o => new TimelineEntry
        {
            TimestampUtc = o.SeenAt.ToUnixTimeMilliseconds(),
            EventType = o.Id == firstObservationId ? "DETECTED" : "RE_DETECTED",
            Detail = $"{o.DamageType}, severity {o.Severity}, stage {o.Stage}, confidence {o.Confidence:P0}",
            ImageUrl = o.ImageUrl ?? ""
        }));
        return response;
    }

    private static Guid ParseId(string damageId) =>
        Guid.TryParse(damageId, out var id)
            ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid damage_id"));

    // The columns the responses need, the area names, and the newest photo.
    private IQueryable<PotholeRow> ToRows(IQueryable<Pothole> potholes) =>
        potholes.Select(p => new PotholeRow(
            p.Id,
            p.CurrentStage,
            p.Location,
            p.RoadName,
            p.Status,
            p.WorkOrderId,
            p.SightingsCount,
            p.FirstSeenAt,
            p.LastSeenAt,
            _db.Observations
                .Where(o => o.PotholeId == p.Id && o.ImageUrl != null) // a correction has no photo
                .OrderByDescending(o => o.SeenAt)
                .Select(o => o.ImageUrl)
                .FirstOrDefault(),
            p.DistrictId,
            _db.AdminAreas.Where(a => a.Id == p.DistrictId).Select(a => a.Name).FirstOrDefault(),
            p.NeighborhoodId,
            _db.AdminAreas.Where(a => a.Id == p.NeighborhoodId).Select(a => a.Name).FirstOrDefault()));

    // SKELETON: the newer DamageDetailResponse fields (status, closed reason, severity, size, estimate,
    // source, work order, repair status) are not filled yet. They come with the pothole details PR (P11).
    private static DamageDetailResponse ToResponse(PotholeRow row) => new()
    {
        DamageId = row.Id.ToString(),
        Stage = (Smartcity.Enums.V1.DamageStage)(int)row.Stage,
        DistrictId = row.DistrictId ?? 0,
        DistrictName = row.DistrictName ?? "",
        NeighborhoodId = row.NeighborhoodId ?? 0,
        NeighborhoodName = row.NeighborhoodName ?? "",
        Location = new GeoCoordinates { Latitude = row.Location.Latitude, Longitude = row.Location.Longitude },
        RoadName = row.RoadName ?? "",
        LatestImageUrl = row.LatestImageUrl ?? "",
        // A ticket is active only while the pothole is open.
        ActiveWorkOrderId = row.Status == PotholeStatus.Open && row.WorkOrderId is not null ? row.WorkOrderId.ToString() : "",
        DetectionCount = row.SightingsCount,
        DetectionTimestampUtc = row.FirstSeenAt.ToUnixTimeMilliseconds(),
        LastSeenTimestampUtc = row.LastSeenAt.ToUnixTimeMilliseconds()
    };

    private sealed record PotholeRow(
        Guid Id,
        Contracts.Enums.DamageStage Stage,
        GeoPoint Location,
        string? RoadName,
        PotholeStatus Status,
        Guid? WorkOrderId,
        int SightingsCount,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        string? LatestImageUrl,
        int? DistrictId,
        string? DistrictName,
        int? NeighborhoodId,
        string? NeighborhoodName);
}
