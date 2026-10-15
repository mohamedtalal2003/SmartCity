namespace SmartCity.Pothole.Infrastructure.RecurringSpots;

/// <summary>
/// The recurring-spots job settings, from the <c>RecurringSpots</c> section of appsettings.
/// SKELETON: all four defaults are first guesses; the design leaves how often and how far back open.
/// </summary>
public sealed class RecurringSpotsOptions
{
    public const string SectionName = "RecurringSpots";

    /// <summary>Only potholes first seen in the last this many days count.</summary>
    public int LookbackDays { get; set; } = 365;

    /// <summary>How often the table is rebuilt. It is also built once when the service starts.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>Potholes this close to each other are the same place.</summary>
    public double RadiusMeters { get; set; } = 3;

    /// <summary>A place needs at least this many potholes.</summary>
    public int MinPotholes { get; set; } = 2;
}
