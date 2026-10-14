namespace SmartCity.Pothole.Domain.Estimation;

/// <summary>
/// The prices the estimator needs. Filled from the <c>PotholePrices</c> section of appsettings,
/// never from code, so the city can change a price without a new build.
/// </summary>
public sealed record PotholePrices
{
    // Total repair price for one pothole, by severity.
    public decimal Low { get; init; }
    public decimal Medium { get; init; }
    public decimal High { get; init; }
    public decimal Critical { get; init; }

    // How the total is split. The three shares add up to 1.
    public decimal MaterialsShare { get; init; }
    public decimal LaborShare { get; init; }
    public decimal EquipmentShare { get; init; }

    public string Currency { get; init; } = "";
}
