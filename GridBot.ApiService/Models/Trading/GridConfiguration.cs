namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current grid configuration state for a market.
/// </summary>
public sealed class GridConfiguration
{
    /// <summary>
    /// Current grid spacing as a percentage (0.15% to 3.0%).
    /// Determined by ATR-based volatility calculation.
    /// </summary>
    public decimal GridSpacing { get; set; } = 0.5m;

    /// <summary>
    /// Number of orders on each side (buy/sell) of the grid (4 to 10).
    /// </summary>
    public int OrdersPerSide { get; set; } = 6;

    /// <summary>
    /// Minimum allowed total grid width as percentage (default 5%).
    /// </summary>
    public decimal MinGridWidth { get; set; } = 5m;

    /// <summary>
    /// Maximum allowed total grid width as percentage (default 30%).
    /// </summary>
    public decimal MaxGridWidth { get; set; } = 30m;

    /// <summary>
    /// Current upper price bound of the grid.
    /// </summary>
    public decimal CurrentUpperBound { get; set; }

    /// <summary>
    /// Current lower price bound of the grid.
    /// </summary>
    public decimal CurrentLowerBound { get; set; }

    /// <summary>
    /// Last calculated ATR (Average True Range) value used for spacing.
    /// Null if ATR calculation not yet available.
    /// </summary>
    public decimal? LastAtrValue { get; set; }

    /// <summary>
    /// When this configuration was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Calculates the current total grid width as a percentage.
    /// </summary>
    public decimal GetCurrentGridWidth()
    {
        if (CurrentLowerBound <= 0)
            return 0;

        return ((CurrentUpperBound - CurrentLowerBound) / CurrentLowerBound) * 100m;
    }

    /// <summary>
    /// Creates a copy of this configuration.
    /// </summary>
    public GridConfiguration Clone()
    {
        return new GridConfiguration
        {
            GridSpacing = GridSpacing,
            OrdersPerSide = OrdersPerSide,
            MinGridWidth = MinGridWidth,
            MaxGridWidth = MaxGridWidth,
            CurrentUpperBound = CurrentUpperBound,
            CurrentLowerBound = CurrentLowerBound,
            LastAtrValue = LastAtrValue,
            LastUpdated = LastUpdated
        };
    }
}
