using SmartCity.Contracts.Enums;
using SmartCity.Contracts.Events.Business;

namespace SmartCity.Contracts.Common;

/// <summary>
/// A repair estimate made by the service that owns the item (Pothole today; Inventory and Building
/// later). Each service has its own estimator; only this shape is shared.
/// </summary>
public record CostEstimateInfo
{
    public decimal Materials { get; init; }                 // material cost
    public decimal Labor { get; init; }                     // labor cost
    public decimal Equipment { get; init; }                 // equipment cost
    public decimal Total { get; init; }                     // Materials + Labor + Equipment
    public string Currency { get; init; } = "TRY";          // ISO currency code
    public double LaborHours { get; init; }                 // expected crew hours on site
    public int CrewSize { get; init; }                      // people needed
    public PriorityLevel SuggestedPriority { get; init; }   // what the estimator suggests for the ticket
    public List<MaterialItem> MaterialItems { get; init; } = new();  // e.g. "Soğuk asfalt", 1.0 m³
}
