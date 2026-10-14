using SmartCity.Contracts.Enums;

namespace SmartCity.Pothole.Domain.Estimation;

/// <summary>The result of <see cref="PotholeEstimator.Estimate"/>.</summary>
public sealed record CostEstimate
{
    public decimal Materials { get; init; }
    public decimal Labor { get; init; }
    public decimal Equipment { get; init; }
    public decimal Total { get; init; }
    public string Currency { get; init; } = "";

    public double LaborHours { get; init; }
    public int CrewSize { get; init; }
    public PriorityLevel SuggestedPriority { get; init; }
    public MaterialLine Material { get; init; } = default!;
}

/// <summary>One material the crew needs, for example cold asphalt in m³.</summary>
public sealed record MaterialLine(string Name, double Quantity, string Unit);
