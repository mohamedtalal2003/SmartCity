using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.VehicleCommunication.Domain.Vehicles;
using SmartCity.VehicleCommunication.Infrastructure.Persistence;
using Smartcity.Common.V1;
using Smartcity.Queries.V1;
using ContractEnums = SmartCity.Contracts.Enums;
using ProtoEnums = Smartcity.Enums.V1;

namespace SmartCity.VehicleCommunication.Api.Grpc;

/// <summary>
/// Read side (V5), called by the Gateway. Queries <see cref="VehicleCommDbContext"/> directly with AsNoTracking
/// (no repository for reads), like Pothole. Pages are ordered by vehicle id; the page token is the last id.
/// </summary>
public sealed class VehicleQueryGrpcService : VehicleQueryService.VehicleQueryServiceBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly VehicleCommDbContext _db;

    public VehicleQueryGrpcService(VehicleCommDbContext db) => _db = db;

    public override async Task<VehicleDetailResponse> GetVehicle(GetVehicleRequest request, ServerCallContext context)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, context.CancellationToken);

        return vehicle is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "Vehicle not found"))
            : ToResponse(vehicle);
    }

    public override async Task<ListVehiclesResponse> ListVehicles(ListVehiclesRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var pageSize = request.Pagination?.PageSize is > 0 and var size ? Math.Min(size, MaxPageSize) : DefaultPageSize;

        // The proto enums have the same numbers as the C# ones (the enum rule), so a cast is enough.
        var query = _db.Vehicles.AsNoTracking();
        if (request.Status != ProtoEnums.VehicleStatus.Unspecified)
        {
            var status = (ContractEnums.VehicleStatus)(int)request.Status;
            query = query.Where(v => v.Status == status);
        }
        if (request.ConnectionStatus != ProtoEnums.VehicleConnectionStatus.Unspecified)
        {
            var connection = (ContractEnums.VehicleConnectionStatus)(int)request.ConnectionStatus;
            query = query.Where(v => v.ConnectionStatus == connection);
        }
        if (request.DistrictIds.Count > 0)
        {
            var districts = request.DistrictIds.ToList();
            query = query.Where(v => v.DistrictId != null && districts.Contains(v.DistrictId.Value));
        }

        var total = await query.CountAsync(ct);

        var afterId = request.Pagination?.PageToken;
        if (!string.IsNullOrEmpty(afterId))
            query = query.Where(v => string.Compare(v.Id, afterId) > 0);

        var vehicles = await query.OrderBy(v => v.Id).Take(pageSize).ToListAsync(ct);

        var response = new ListVehiclesResponse
        {
            Pagination = new PaginationResponse
            {
                NextPageToken = vehicles.Count == pageSize ? vehicles[^1].Id : "",
                TotalCount = total
            }
        };
        response.Records.AddRange(vehicles.Select(ToResponse));
        return response;
    }

    private static VehicleDetailResponse ToResponse(Vehicle v) => new()
    {
        VehicleId = v.Id,
        Plate = v.Plate,
        DistrictId = v.DistrictId ?? 0,
        Notes = v.Notes,
        Status = (ProtoEnums.VehicleStatus)(int)v.Status,
        ConnectionStatus = (ProtoEnums.VehicleConnectionStatus)(int)v.ConnectionStatus,
        LastSeenAtUtc = v.LastSeenAt?.ToUnixTimeMilliseconds() ?? 0,
        LastLatitude = v.LastLatitude ?? 0,
        LastLongitude = v.LastLongitude ?? 0,
        PendingFrames = v.PendingFrames,
        CreatedAtUtc = v.CreatedAt.ToUnixTimeMilliseconds()
    };
}
