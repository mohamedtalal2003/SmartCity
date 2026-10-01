using SmartCity.Contracts.Enums;

namespace SmartCity.Notification.Api.Domain;

public class NotificationRecord
{
    public Guid Id { get; set; }
    public Guid CorrelationId { get; set; }
    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;
    public Guid RelatedEntityId { get; set; }
    public SourceEntityType RelatedEntityType { get; set; }
    public string Recipient { get; set; } = "dashboard";
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
