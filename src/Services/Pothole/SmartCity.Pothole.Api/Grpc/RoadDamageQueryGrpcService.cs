using System.Globalization;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCity.Pothole.Infrastructure.Persistence;
using Smartcity.Common.V1;
using Smartcity.Queries.V1;

namespace SmartCity.Pothole.Api.Grpc;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Read side (P11–P14). Queries <see cref="PotholeDbContext"/> directly with AsNoTracking (no repository for
/// reads) and maps to the proto messages. The filters and the cursor are in <see cref="DamageFilters"/>.
/// </summary>
public sealed class RoadDamageQueryGrpcService : RoadDamageQueryService.RoadDamageQueryServiceBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly PotholeDbContext _db;
    private readonly MapOptions _map;

    public RoadDamageQueryGrpcService(PotholeDbContext db, IOptions<MapOptions> map)
    {
        _db = db;
        _map = map.Value;
    }

    // P11: one pothole.
    public override async Task<DamageDetailResponse> GetDamageById(GetDamageRequest request, ServerCallContext context)
    {
        var id = ParseId(request.DamageId);

        var row = await ToRows(_db.Potholes.AsNoTracking().Where(p => p.Id == id))
            .FirstOrDefaultAsync(context.CancellationToken);
        if (row is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Pothole not found"));

        return ToResponse(row);
    }

    // P12: the list, with every filter, the sort and cursor pages.
    public override async Task<ListDamagesResponse> ListDamages(ListDamagesRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var query = DamageFilters.Apply(DamageFilters.InBox(_db, request.MapArea), request);
        var pageSize = PageSize(request.Pagination);

        // OldestUnrepaired also limits the list to open potholes, so count after the sort's own filter.
        var totalCount = await DamageFilters.SortAfter(query, request.Sort, pageToken: null).CountAsync(ct);

        var page = DamageFilters.SortAfter(query, request.Sort, request.Pagination?.PageToken).Take(pageSize);
        var rows = await ToRows(page).ToListAsync(ct);

        var last = rows.LastOrDefault();
        var response = new ListDamagesResponse
        {
            Pagination = new PaginationResponse
            {
                TotalCount = totalCount,
                NextPageToken = rows.Count == pageSize && last is not null
                    ? DamageFilters.CursorAfter(request.Sort, last.Severity, last.EstTotal, last.FirstSeenAt, last.Id)
                    : ""
            }
        };
        response.Records.AddRange(rows.Select(ToResponse));
        return response;
    }

    // P13: the map. Few potholes in the box: one point each. Many: one circle per grid cell.
    public override async Task<DamagesOnMapResponse> GetDamagesOnMap(DamagesOnMapRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var filters = request.Filters ?? new ListDamagesRequest();
        var query = DamageFilters.Apply(DamageFilters.InBox(_db, request.MapArea), filters);
        var response = new DamagesOnMapResponse();

        var count = await query.CountAsync(ct);
        if (count < _map.MaxPoints)
        {
            var points = await query
                .Select(p => new { p.Id, p.Location, p.CurrentSeverity, p.Status })
                .ToListAsync(ct);
            response.Points.AddRange(points.Select(p => new DamagePoint
            {
                DamageId = p.Id.ToString(),
                Location = new GeoCoordinates { Latitude = p.Location.Latitude, Longitude = p.Location.Longitude },
                Severity = (Smartcity.Enums.V1.SeverityLevel)(int)p.CurrentSeverity,
                Status = Enum.Parse<Smartcity.Enums.V1.PotholeStatus>(p.Status.ToString())
            }));
            return response;
        }

        // SKELETON: the matching ids are loaded first and passed to the grid query, so the LINQ filters are
        // not written twice in SQL. The real version puts the filters in the same SQL.
        var ids = await query.Select(p => p.Id).ToArrayAsync(ct);
        var cellDegrees = CellSizeDegrees(request.Zoom);
        var clusters = await _db.Database
            .SqlQuery<ClusterRow>($"""
                SELECT ST_Y(ST_Centroid(ST_Collect(location::geometry))) AS "Latitude",
                       ST_X(ST_Centroid(ST_Collect(location::geometry))) AS "Longitude",
                       count(*)::int AS "Count",
                       max(CASE current_severity
                             WHEN 'Critical' THEN 4 WHEN 'High' THEN 3 WHEN 'Medium' THEN 2 WHEN 'Low' THEN 1
                             ELSE 0 END) AS "MaxSeverity"
                FROM potholes
                WHERE id = ANY({ids})
                GROUP BY ST_SnapToGrid(location::geometry, {cellDegrees})
                """)
            .ToListAsync(ct);

        response.Clusters.AddRange(clusters.Select(c => new DamageCluster
        {
            Center = new GeoCoordinates { Latitude = c.Latitude, Longitude = c.Longitude },
            Count = c.Count,
            MaxSeverity = (Smartcity.Enums.V1.SeverityLevel)c.MaxSeverity
        }));
        return response;
    }

    // P14: the history of one pothole, newest first, in cursor pages.
    public override async Task<DamageTimelineResponse> GetDamageTimeline(GetDamageTimelineRequest request, ServerCallContext context)
    {
        var id = ParseId(request.DamageId);
        var ct = context.CancellationToken;

        var pothole = await _db.Potholes.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Status, p.ClosedReason, p.ClosedAt, p.DuplicateOfId })
            .FirstOrDefaultAsync(ct);
        if (pothole is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Pothole not found"));

        // The first observation is when the pothole was found; the rest came later.
        var firstObservationId = await _db.Observations
            .Where(o => o.PotholeId == id)
            .OrderBy(o => o.SeenAt).ThenBy(o => o.Id)
            .Select(o => o.Id)
            .FirstAsync(ct);
        var totalCount = await _db.Observations.CountAsync(o => o.PotholeId == id, ct);

        // Keyset on (seen_at, id): the cursor is "seen_at|id" of the last entry of the previous page.
        var pageSize = PageSize(request.Pagination);
        var query = _db.Observations.AsNoTracking().Where(o => o.PotholeId == id);
        var cursor = DamageFilters.ParseCursor(request.Pagination?.PageToken);
        if (cursor is not null)
        {
            var seenAt = DateTimeOffset.Parse(cursor.Value.Value, CultureInfo.InvariantCulture);
            var afterId = cursor.Value.Id;
            query = query.Where(o => o.SeenAt < seenAt || (o.SeenAt == seenAt && o.Id.CompareTo(afterId) > 0));
        }
        var observations = await query
            .OrderByDescending(o => o.SeenAt).ThenBy(o => o.Id)
            .Take(pageSize)
            .ToListAsync(ct);

        var response = new DamageTimelineResponse { DamageId = id.ToString() };

        // The closing heads the first page.
        if (cursor is null && pothole.Status == PotholeStatus.Closed && pothole.ClosedAt is not null)
        {
            response.Entries.Add(new TimelineEntry
            {
                TimestampUtc = pothole.ClosedAt.Value.ToUnixTimeMilliseconds(),
                EventType = "CLOSED",
                Detail = pothole.ClosedReason == ClosedReason.Duplicate
                    ? $"Closed as Duplicate of {pothole.DuplicateOfId}"
                    : $"Closed as {pothole.ClosedReason}"
            });
        }

        response.Entries.AddRange(observations.Select(o => new TimelineEntry
        {
            TimestampUtc = o.SeenAt.ToUnixTimeMilliseconds(),
            EventType = EventType(o, firstObservationId),
            Detail = Detail(o),
            ImageUrl = o.ImageUrl ?? "",
            DamageType = (Smartcity.Enums.V1.DetectionType)(int)o.DamageType,
            Severity = (Smartcity.Enums.V1.SeverityLevel)(int)o.Severity,
            Stage = (Smartcity.Enums.V1.DamageStage)(int)o.Stage,
            Kind = o.Kind.ToString()
        }));

        var last = observations.LastOrDefault();
        response.Pagination = new PaginationResponse
        {
            TotalCount = totalCount,
            NextPageToken = observations.Count == pageSize && last is not null
                ? $"{last.SeenAt.ToString("O", CultureInfo.InvariantCulture)}|{last.Id}"
                : ""
        };
        return response;
    }

    private static string EventType(Observation o, Guid firstObservationId)
    {
        if (o.Id == firstObservationId)
            return "DETECTED";
        return o.Kind switch
        {
            ObservationKind.Correction => "CORRECTED",
            ObservationKind.VerificationPass => "VERIFICATION",
            _ => "RE_DETECTED"
        };
    }

    private static string Detail(Observation o) => o.Kind switch
    {
        ObservationKind.OfficerAdd =>
            $"Reported by {o.CreatedBy}: severity {o.Severity}" + (o.Notes is null ? "" : $" ({o.Notes})"),
        ObservationKind.Correction =>
            $"Corrected by {o.CreatedBy}: severity {o.Severity}, stage {o.Stage}. Reason: {o.Notes}",
        ObservationKind.VerificationPass =>
            $"Verification: {o.VerificationResult}, confidence {o.Confidence:P0}",
        _ => $"{o.DamageType}, severity {o.Severity}, stage {o.Stage}, confidence {o.Confidence:P0}"
    };

    // A 256-pixel map tile spans 360 / 2^zoom degrees; it is cut into CellsPerTile × CellsPerTile cells.
    private double CellSizeDegrees(int zoom) => 360.0 / Math.Pow(2, Math.Clamp(zoom, 0, 22)) / _map.CellsPerTile;

    private static int PageSize(PaginationRequest? pagination) =>
        pagination?.PageSize > 0 ? Math.Min(pagination.PageSize, MaxPageSize) : DefaultPageSize;

    private static Guid ParseId(string damageId) =>
        Guid.TryParse(damageId, out var id)
            ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid damage_id"));

    // The columns the responses need, the area names, and the newest photo.
    private IQueryable<PotholeRow> ToRows(IQueryable<Pothole> potholes) =>
        potholes.Select(p => new PotholeRow
        {
            Id = p.Id,
            DamageType = p.DamageType,
            Stage = p.CurrentStage,
            Severity = p.CurrentSeverity,
            Location = p.Location,
            RoadName = p.RoadName,
            Status = p.Status,
            ClosedReason = p.ClosedReason,
            ClosedAt = p.ClosedAt,
            DuplicateOfId = p.DuplicateOfId,
            AreaM2 = p.AreaM2,
            DepthCm = p.DepthCm,
            Confidence = p.Confidence,
            Source = p.Source,
            EstTotal = p.Estimate.Total,
            EstMaterials = p.Estimate.Materials,
            EstLabor = p.Estimate.Labor,
            EstEquipment = p.Estimate.Equipment,
            Currency = p.Estimate.Currency,
            WorkOrderId = p.WorkOrderId,
            RepairStatus = p.RepairStatus,
            SightingsCount = p.SightingsCount,
            FirstSeenAt = p.FirstSeenAt,
            LastSeenAt = p.LastSeenAt,
            LatestImageUrl = _db.Observations
                .Where(o => o.PotholeId == p.Id && o.ImageUrl != null) // a correction has no photo
                .OrderByDescending(o => o.SeenAt)
                .Select(o => o.ImageUrl)
                .FirstOrDefault(),
            DistrictId = p.DistrictId,
            DistrictName = _db.AdminAreas.Where(a => a.Id == p.DistrictId).Select(a => a.Name).FirstOrDefault(),
            NeighborhoodId = p.NeighborhoodId,
            NeighborhoodName = _db.AdminAreas.Where(a => a.Id == p.NeighborhoodId).Select(a => a.Name).FirstOrDefault()
        });

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
        LastSeenTimestampUtc = row.LastSeenAt.ToUnixTimeMilliseconds(),

        // The Pothole domain enums have the same names as the proto enums, so they are mapped by name.
        // The shared Contracts enums have the same numbers, so they are cast.
        Status = Enum.Parse<Smartcity.Enums.V1.PotholeStatus>(row.Status.ToString()),
        ClosedReason = row.ClosedReason is null
            ? Smartcity.Enums.V1.ClosedReason.Unspecified
            : Enum.Parse<Smartcity.Enums.V1.ClosedReason>(row.ClosedReason.Value.ToString()),
        ClosedAtUtc = row.ClosedAt?.ToUnixTimeMilliseconds() ?? 0,
        DuplicateOfId = row.DuplicateOfId?.ToString() ?? "",
        Severity = (Smartcity.Enums.V1.SeverityLevel)(int)row.Severity,
        DamageType = (Smartcity.Enums.V1.DetectionType)(int)row.DamageType,
        AreaM2 = row.AreaM2 ?? 0,
        DepthCm = row.DepthCm ?? 0, // cm, as stored
        Confidence = row.Confidence,
        Source = Enum.Parse<Smartcity.Enums.V1.PotholeSource>(row.Source.ToString()),
        EstimatedTotal = (double)row.EstTotal,
        EstimatedMaterials = (double)row.EstMaterials,
        EstimatedLabor = (double)row.EstLabor,
        EstimatedEquipment = (double)row.EstEquipment,
        Currency = row.Currency,
        WorkOrderId = row.WorkOrderId?.ToString() ?? "",
        RepairStatus = row.RepairStatus ?? ""
    };

    private sealed class PotholeRow
    {
        public Guid Id { get; init; }
        public Contracts.Enums.DetectionType DamageType { get; init; }
        public Contracts.Enums.DamageStage Stage { get; init; }
        public Contracts.Enums.SeverityLevel Severity { get; init; }
        public GeoPoint Location { get; init; } = default!;
        public string? RoadName { get; init; }
        public PotholeStatus Status { get; init; }
        public ClosedReason? ClosedReason { get; init; }
        public DateTimeOffset? ClosedAt { get; init; }
        public Guid? DuplicateOfId { get; init; }
        public double? AreaM2 { get; init; }
        public double? DepthCm { get; init; }
        public double Confidence { get; init; }
        public PotholeSource Source { get; init; }
        public decimal EstTotal { get; init; }
        public decimal EstMaterials { get; init; }
        public decimal EstLabor { get; init; }
        public decimal EstEquipment { get; init; }
        public string Currency { get; init; } = "";
        public Guid? WorkOrderId { get; init; }
        public string? RepairStatus { get; init; }
        public int SightingsCount { get; init; }
        public DateTimeOffset FirstSeenAt { get; init; }
        public DateTimeOffset LastSeenAt { get; init; }
        public string? LatestImageUrl { get; init; }
        public int? DistrictId { get; init; }
        public string? DistrictName { get; init; }
        public int? NeighborhoodId { get; init; }
        public string? NeighborhoodName { get; init; }
    }

    // One grid cell of the map query (the property names match the SQL column aliases).
    private sealed class ClusterRow
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Count { get; set; }
        public int MaxSeverity { get; set; }
    }
}
