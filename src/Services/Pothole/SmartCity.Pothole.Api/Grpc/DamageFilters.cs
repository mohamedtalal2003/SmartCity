using System.Globalization;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Infrastructure.Persistence;
using Smartcity.Common.V1;
using Smartcity.Queries.V1;

namespace SmartCity.Pothole.Api.Grpc;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// The pothole list filters (P12), the sort orders and the cursor. Shared by the list and the map.
/// Every filter is optional: an empty list or a 0 means "no limit".
/// </summary>
internal static class DamageFilters
{
    private static readonly SeverityLevel[] Severities =
        { SeverityLevel.Low, SeverityLevel.Medium, SeverityLevel.High, SeverityLevel.Critical };

    /// <summary>All potholes, or only the ones inside the box.</summary>
    public static IQueryable<Pothole> InBox(PotholeDbContext db, BoundingBox? box)
    {
        if (box?.SouthWest is null || box.NorthEast is null)
            return db.Potholes.AsNoTracking();

        // The location is a PostGIS geography, so the box test is SQL. The other filters are added on top.
        var sw = box.SouthWest;
        var ne = box.NorthEast;
        return db.Potholes
            .FromSqlInterpolated($"""
                SELECT * FROM potholes
                WHERE ST_Intersects(location, ST_MakeEnvelope({sw.Longitude}, {sw.Latitude}, {ne.Longitude}, {ne.Latitude}, 4326)::geography)
                """)
            .AsNoTracking();
    }

    public static IQueryable<Pothole> Apply(IQueryable<Pothole> q, ListDamagesRequest r)
    {
        // Place
        if (r.DistrictIds.Count > 0)
        {
            var ids = r.DistrictIds.ToList();
            q = q.Where(p => p.DistrictId != null && ids.Contains(p.DistrictId.Value));
        }
        if (r.NeighborhoodIds.Count > 0)
        {
            var ids = r.NeighborhoodIds.ToList();
            q = q.Where(p => p.NeighborhoodId != null && ids.Contains(p.NeighborhoodId.Value));
        }
        if (!string.IsNullOrWhiteSpace(r.RoadName))
            q = q.Where(p => p.RoadName != null && EF.Functions.ILike(p.RoadName, $"%{r.RoadName.Trim()}%"));

        // State
        if (r.Statuses.Count > 0)
        {
            var statuses = r.Statuses.Where(s => s != Smartcity.Enums.V1.PotholeStatus.Unspecified)
                .Select(s => Enum.Parse<PotholeStatus>(s.ToString())).ToList();
            q = q.Where(p => statuses.Contains(p.Status));
        }
        if (r.ClosedReasons.Count > 0)
        {
            var reasons = r.ClosedReasons.Where(c => c != Smartcity.Enums.V1.ClosedReason.Unspecified)
                .Select(c => (ClosedReason?)Enum.Parse<ClosedReason>(c.ToString())).ToList();
            q = q.Where(p => reasons.Contains(p.ClosedReason));
        }
        if (r.RepairStatuses.Count > 0)
        {
            var repairStatuses = r.RepairStatuses.ToList();
            q = q.Where(p => p.RepairStatus != null && repairStatuses.Contains(p.RepairStatus));
        }
        if (r.RequiresRepairOnly)
            q = q.Where(p => p.Status == PotholeStatus.Open);
        if (r.WithoutActiveWorkOrder)
            q = q.Where(p => p.Status == PotholeStatus.Open && p.WorkOrderId == null);

        // The pothole. Stage and severity are stored as text, so "at least" becomes a list of the allowed values.
        if (r.Stages.Count > 0)
        {
            var stages = r.Stages.Select(s => (DamageStage)(int)s).ToList();
            q = q.Where(p => stages.Contains(p.CurrentStage));
        }
        if (r.MinStage != Smartcity.Enums.V1.DamageStage.Unspecified)
        {
            var stages = Enum.GetValues<DamageStage>().Where(s => (int)s >= (int)r.MinStage).ToList();
            q = q.Where(p => stages.Contains(p.CurrentStage));
        }
        if (r.MinSeverity != Smartcity.Enums.V1.SeverityLevel.Unspecified)
        {
            var severities = Severities.Where(s => (int)s >= (int)r.MinSeverity).ToList();
            q = q.Where(p => severities.Contains(p.CurrentSeverity));
        }
        if (r.MinAreaM2 > 0) q = q.Where(p => p.AreaM2 >= r.MinAreaM2);
        if (r.MaxAreaM2 > 0) q = q.Where(p => p.AreaM2 <= r.MaxAreaM2);
        if (r.MinDepthCm > 0) q = q.Where(p => p.DepthCm >= r.MinDepthCm);
        if (r.MaxDepthCm > 0) q = q.Where(p => p.DepthCm <= r.MaxDepthCm);
        if (r.MinConfidence > 0) q = q.Where(p => p.Confidence >= r.MinConfidence);

        // Money and source
        if (r.MinCost > 0)
        {
            var minCost = (decimal)r.MinCost;
            q = q.Where(p => p.Estimate.Total >= minCost);
        }
        if (r.MaxCost > 0)
        {
            var maxCost = (decimal)r.MaxCost;
            q = q.Where(p => p.Estimate.Total <= maxCost);
        }
        if (r.Sources.Count > 0)
        {
            var sources = r.Sources.Where(s => s != Smartcity.Enums.V1.PotholeSource.Unspecified)
                .Select(s => Enum.Parse<PotholeSource>(s.ToString())).ToList();
            q = q.Where(p => sources.Contains(p.Source));
        }
        if (r.MinSightings > 0) q = q.Where(p => p.SightingsCount >= r.MinSightings);

        // Time (epoch ms, 0 = open end)
        if (r.TimeRange is { } firstSeen)
        {
            if (firstSeen.FromUtc > 0) { var from = FromMs(firstSeen.FromUtc); q = q.Where(p => p.FirstSeenAt >= from); }
            if (firstSeen.ToUtc > 0) { var to = FromMs(firstSeen.ToUtc); q = q.Where(p => p.FirstSeenAt <= to); }
        }
        if (r.LastSeen is { } lastSeen)
        {
            if (lastSeen.FromUtc > 0) { var from = FromMs(lastSeen.FromUtc); q = q.Where(p => p.LastSeenAt >= from); }
            if (lastSeen.ToUtc > 0) { var to = FromMs(lastSeen.ToUtc); q = q.Where(p => p.LastSeenAt <= to); }
        }
        if (r.Closed is { } closed)
        {
            if (closed.FromUtc > 0) { var from = FromMs(closed.FromUtc); q = q.Where(p => p.ClosedAt >= from); }
            if (closed.ToUtc > 0) { var to = FromMs(closed.ToUtc); q = q.Where(p => p.ClosedAt <= to); }
        }

        return q;
    }

    /// <summary>
    /// Orders the potholes and skips to the cursor. The cursor is "sort value|id" of the last row of the
    /// previous page (a keyset), so a new pothole never shifts the pages.
    /// </summary>
    public static IQueryable<Pothole> SortAfter(IQueryable<Pothole> q, Smartcity.Enums.V1.DamageSort sort, string? pageToken)
    {
        var cursor = ParseCursor(pageToken);

        switch (sort)
        {
            case Smartcity.Enums.V1.DamageSort.MostSevere:
                if (cursor is not null)
                {
                    var severity = Enum.Parse<SeverityLevel>(cursor.Value.Value);
                    var lower = Severities.Where(s => s < severity).ToList();
                    var id = cursor.Value.Id;
                    q = q.Where(p => lower.Contains(p.CurrentSeverity) || (p.CurrentSeverity == severity && p.Id.CompareTo(id) > 0));
                }
                return q
                    .OrderByDescending(p => p.CurrentSeverity == SeverityLevel.Critical ? 4
                        : p.CurrentSeverity == SeverityLevel.High ? 3
                        : p.CurrentSeverity == SeverityLevel.Medium ? 2
                        : 1)
                    .ThenBy(p => p.Id);

            case Smartcity.Enums.V1.DamageSort.MostExpensive:
                if (cursor is not null)
                {
                    var total = decimal.Parse(cursor.Value.Value, CultureInfo.InvariantCulture);
                    var id = cursor.Value.Id;
                    q = q.Where(p => p.Estimate.Total < total || (p.Estimate.Total == total && p.Id.CompareTo(id) > 0));
                }
                return q.OrderByDescending(p => p.Estimate.Total).ThenBy(p => p.Id);

            case Smartcity.Enums.V1.DamageSort.OldestUnrepaired:
                // Only open potholes, the one waiting the longest first.
                q = q.Where(p => p.Status == PotholeStatus.Open);
                if (cursor is not null)
                {
                    var firstSeen = DateTimeOffset.Parse(cursor.Value.Value, CultureInfo.InvariantCulture);
                    var id = cursor.Value.Id;
                    q = q.Where(p => p.FirstSeenAt > firstSeen || (p.FirstSeenAt == firstSeen && p.Id.CompareTo(id) > 0));
                }
                return q.OrderBy(p => p.FirstSeenAt).ThenBy(p => p.Id);

            default: // Newest (also when not set)
                if (cursor is not null)
                {
                    var firstSeen = DateTimeOffset.Parse(cursor.Value.Value, CultureInfo.InvariantCulture);
                    var id = cursor.Value.Id;
                    q = q.Where(p => p.FirstSeenAt < firstSeen || (p.FirstSeenAt == firstSeen && p.Id.CompareTo(id) > 0));
                }
                return q.OrderByDescending(p => p.FirstSeenAt).ThenBy(p => p.Id);
        }
    }

    /// <summary>The cursor for the page after this row.</summary>
    public static string CursorAfter(Smartcity.Enums.V1.DamageSort sort, SeverityLevel severity, decimal total, DateTimeOffset firstSeenAt, Guid id)
    {
        var value = sort switch
        {
            Smartcity.Enums.V1.DamageSort.MostSevere => severity.ToString(),
            Smartcity.Enums.V1.DamageSort.MostExpensive => total.ToString(CultureInfo.InvariantCulture),
            _ => firstSeenAt.ToString("O", CultureInfo.InvariantCulture)
        };
        return $"{value}|{id}";
    }

    /// <summary>"value|id" → its two parts. A broken cursor is the caller's mistake.</summary>
    public static (string Value, Guid Id)? ParseCursor(string? pageToken)
    {
        if (string.IsNullOrEmpty(pageToken))
            return null;

        var bar = pageToken.LastIndexOf('|');
        if (bar < 0 || !Guid.TryParse(pageToken[(bar + 1)..], out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid page_token"));
        return (pageToken[..bar], id);
    }

    private static DateTimeOffset FromMs(long epochMs) => DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
}
