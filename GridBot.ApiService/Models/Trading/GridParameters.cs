namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Parameters defining the current grid configuration.
/// Calculated from market conditions (ATR, price).
/// </summary>
public sealed class GridParameters
{
    /// <summary>
    /// Center price of the grid (current market price when calculated).
    /// </summary>
    public required decimal CenterPrice { get; init; }

    /// <summary>
    /// Spacing between grid levels as a percentage (e.g., 0.5 = 0.5%).
    /// Range: 0.15% to 3.0% based on ATR.
    /// </summary>
    public required decimal GridSpacing { get; init; }

    /// <summary>
    /// Number of orders to place on each side (bids and asks).
    /// Range: 4 to 10 based on ATR.
    /// </summary>
    public required int OrdersPerSide { get; init; }

    /// <summary>
    /// Upper price boundary of the grid.
    /// </summary>
    public required decimal UpperBound { get; init; }

    /// <summary>
    /// Lower price boundary of the grid.
    /// </summary>
    public required decimal LowerBound { get; init; }

    /// <summary>
    /// Total grid width as a percentage of center price.
    /// </summary>
    public required decimal TotalWidth { get; init; }

    /// <summary>
    /// Timestamp when these parameters were calculated.
    /// </summary>
    public DateTimeOffset CalculatedAt { get; init; } = DateTimeOffset.UtcNow;
}
