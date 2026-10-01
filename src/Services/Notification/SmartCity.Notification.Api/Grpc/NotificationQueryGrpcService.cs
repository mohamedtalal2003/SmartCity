using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SmartCity.Notification.Api.Data;
using Smartcity.Queries.V1;
using Smartcity.Common.V1;

namespace SmartCity.Notification.Api.Grpc;

public sealed class NotificationQueryGrpcService : NotificationQueryService.NotificationQueryServiceBase
{
    private readonly NotificationDbContext _db;

    public NotificationQueryGrpcService(NotificationDbContext db) => _db = db;

    public override async Task<ListNotificationsResponse> ListMyNotifications(
        ListNotificationsRequest request, ServerCallContext context)
    {
        var query = _db.Notifications.AsNoTracking().AsQueryable();

        if (request.UnreadOnly)
            query = query.Where(n => !n.IsRead);

        var pageSize = request.Pagination?.PageSize > 0 ? Math.Min(request.Pagination.PageSize, 100) : 20;
        var totalCount = await query.CountAsync(context.CancellationToken);

        if (!string.IsNullOrEmpty(request.Pagination?.PageToken) &&
            Guid.TryParse(request.Pagination.PageToken, out var cursor))
            query = query.Where(n => n.Id.CompareTo(cursor) > 0);

        var records = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListNotificationsResponse
        {
            Pagination = new PaginationResponse
            {
                TotalCount = totalCount,
                NextPageToken = records.Count == pageSize ? records[^1].Id.ToString() : ""
            }
        };

        response.Records.AddRange(records.Select(n => new NotificationItem
        {
            NotificationId = n.Id.ToString(),
            Title = n.Title,
            Body = n.Body,
            RelatedEntityId = n.RelatedEntityId.ToString(),
            RelatedEntityType = (Smartcity.Enums.V1.SourceEntityType)(int)n.RelatedEntityType,
            IsRead = n.IsRead,
            CreatedAtUtc = n.CreatedAt.ToUnixTimeMilliseconds(),
        }));

        return response;
    }
}
