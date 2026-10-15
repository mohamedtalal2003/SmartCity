using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.Pothole.Infrastructure.Persistence;
using Smartcity.Queries.V1;

namespace SmartCity.Pothole.Api.Grpc;

/// <summary>
/// The Istanbul districts and neighborhoods from <c>admin_areas</c>, for the screen's dropdowns.
/// Read side: queries <see cref="PotholeDbContext"/> directly with AsNoTracking.
/// </summary>
public sealed class AreaQueryGrpcService : AreaQueryService.AreaQueryServiceBase
{
    private readonly PotholeDbContext _db;

    public AreaQueryGrpcService(PotholeDbContext db) => _db = db;

    public override async Task<ListAreasResponse> ListAreas(ListAreasRequest request, ServerCallContext context)
    {
        // No parent: the districts. A district id: its neighborhoods.
        var query = request.HasParentId
            ? _db.AdminAreas.AsNoTracking().Where(a => a.ParentId == request.ParentId)
            : _db.AdminAreas.AsNoTracking().Where(a => a.Level == AdminLevel.District);

        var areas = await query
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, a.Level, a.ParentId })
            .ToListAsync(context.CancellationToken);

        var response = new ListAreasResponse();
        response.Areas.AddRange(areas.Select(a => new Area
        {
            Id = a.Id,
            Name = a.Name,
            Level = a.Level.ToString(),
            ParentId = a.ParentId ?? 0
        }));
        return response;
    }
}
