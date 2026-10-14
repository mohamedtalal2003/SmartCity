namespace SmartCity.Contracts.Enums;

// Mirrors Protos/enums.proto (same names, same numbers).

/// <summary>Pothole state. Closed is final.</summary>
public enum PotholeStatus
{
    Unspecified = 0,
    Open = 1,
    Closed = 2
}

/// <summary>Why a pothole was closed.</summary>
public enum ClosedReason
{
    Unspecified = 0,
    Repaired = 1,           // the ticket was completed
    FalsePositive = 2,      // the AI was wrong (starts the 30-day rule)
    Duplicate = 3,          // the same pothole as another one
    ResolvedOtherwise = 4   // e.g. the whole street was repaved
}

/// <summary>Sort order for pothole lists.</summary>
public enum DamageSort
{
    Unspecified = 0,        // server default (newest first)
    Newest = 1,
    MostSevere = 2,
    MostExpensive = 3,
    OldestUnrepaired = 4
}

/// <summary>Who reported a pothole first.</summary>
public enum PotholeSource
{
    Unspecified = 0,
    Ai = 1,             // a vehicle camera + the cloud AI
    Officer = 2,        // an officer added it by hand
    CitizenReport = 3   // an officer added it from a citizen's report
}

/// <summary>Why PotholeSaved was published.</summary>
public enum PotholeSavedReason
{
    Unspecified = 0,
    New = 1,            // a new pothole was created
    Changed = 2,        // the severity, stage or damage type changed after a new sighting
    Corrected = 3       // an officer corrected its data
}
