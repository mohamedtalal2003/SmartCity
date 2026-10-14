using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Ingestion;

/// <summary>
/// Published by AI Detection for every photo taken at a place that waits for repair confirmation
/// (see VerificationRequested): is the damage gone, still there, or not visible? (scenario P10)
///
/// Subscribers: Work Order (two "Gone" results after the proof time complete the ticket),
/// Pothole (saves the result as a verification-pass observation; the newest "Gone" photo is the
/// "after" photo). Not published by anyone yet.
///
/// CorrelationId = the FrameId of the photo.
/// </summary>
public record RepairVerificationObserved : IntegrationEvent
{
    public Guid WorkOrderId { get; init; }                      // the ticket being checked
    public Guid SourceEntityId { get; init; }                   // the repaired item (PotholeId, ...)
    public SourceEntityType SourceEntityType { get; init; }     // RoadDamage, AssetDamage, ...
    public Guid FrameId { get; init; }                          // the photo that was checked
    public string VehicleId { get; init; } = default!;          // the vehicle that took it
    public VerificationResult Result { get; init; }             // Gone, StillThere or NotVisible
    public double Confidence { get; init; }                     // AI confidence 0.0 – 1.0
    public string ImageUrl { get; init; } = default!;           // the photo's blob URL
    public DateTimeOffset ObservedAt { get; init; }             // when the photo was taken (UTC)
}
