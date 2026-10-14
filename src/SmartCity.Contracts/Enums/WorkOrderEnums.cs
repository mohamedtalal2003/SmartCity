namespace SmartCity.Contracts.Enums;

/// <summary>
/// Mirrors Protos/enums.proto. Main path used by the walking skeleton:
/// Pending → Assigned → EnRoute → OnSite → (Paused ↔ OnSite) → Completed.
/// InProgress, AwaitingReview, Rejected and Cancelled exist in the proto for later use;
/// the skeleton's transition table does not allow entering them yet.
/// </summary>
public enum WorkOrderStatus
{
    Unspecified = 0,
    Pending = 1,
    Assigned = 2,
    InProgress = 3,
    AwaitingReview = 4,
    Completed = 5,
    Rejected = 6,
    EnRoute = 7,
    OnSite = 8,
    Paused = 9,
    Cancelled = 10      // closed without a repair; the reason is in CancelReason
}

/// <summary>Why a ticket was cancelled. The source item (e.g. the pothole) closes with this reason.</summary>
public enum CancelReason
{
    Unspecified = 0,
    FalsePositive = 1,      // the AI was wrong; nothing is there
    Duplicate = 2,          // the same item already has a ticket (see DuplicateOfEntityId)
    ResolvedOtherwise = 3   // fixed another way, e.g. the whole street was repaved
}

public enum PauseReason
{
    Unspecified = 0,
    OutOfMaterials = 1,
    Weather = 2,
    EquipmentFailure = 3,
    SiteInaccessible = 4,
    Other = 5
}

public enum WorkOrderCategory
{
    Unspecified = 0,
    RoadRepair = 1,
    AssetReplacement = 2,
    SignageRemoval = 3,
    ConstructionInspection = 4
}
