namespace SmartCity.Contracts.Enums;

// Mirrors Protos/enums.proto (same names, same numbers).

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
