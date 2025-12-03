namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current inventory allocation state for trend-based position management.
/// </summary>
public sealed class InventoryState
{
    /// <summary>
    /// Current percentage allocated to crypto (0 to 100).
    /// </summary>
    public decimal CryptoAllocation { get; set; } = 50m;

    /// <summary>
    /// Current percentage allocated to USDT (0 to 100).
    /// Should equal 100 - CryptoAllocation.
    /// </summary>
    public decimal UsdtAllocation { get; set; } = 50m;

    /// <summary>
    /// Target crypto percentage based on current trend state (0 to 100).
    /// </summary>
    public decimal TargetSkew { get; set; } = 50m;

    /// <summary>
    /// Current crypto percentage (same as CryptoAllocation, for clarity).
    /// </summary>
    public decimal CurrentSkew { get; set; } = 50m;

    /// <summary>
    /// Whether a rebalance is needed based on tolerance threshold.
    /// True if |CurrentSkew - TargetSkew| > tolerance (typically 5%).
    /// </summary>
    public bool RebalanceNeeded { get; set; }

    /// <summary>
    /// The delta needed to reach target skew.
    /// Positive = need more crypto, Negative = need more USDT.
    /// </summary>
    public decimal RebalanceDelta { get; set; }

    /// <summary>
    /// Current trend state determining the target skew.
    /// </summary>
    public TrendState TrendState { get; set; } = TrendState.Neutral;

    /// <summary>
    /// When the trend state last changed. Null if never changed.
    /// </summary>
    public DateTimeOffset? LastTrendChange { get; set; }

    /// <summary>
    /// Returns the target skew percentage for a given trend state.
    /// </summary>
    public static decimal GetTargetSkewForTrend(TrendState trend)
    {
        return trend switch
        {
            TrendState.StrongBull => 80m,
            TrendState.MildBull => 70m,
            TrendState.Neutral => 50m,
            TrendState.MildBear => 30m,
            TrendState.StrongBear => 20m,
            _ => 50m
        };
    }

    /// <summary>
    /// Calculates whether rebalancing is needed based on tolerance.
    /// </summary>
    /// <param name="tolerancePercent">Tolerance threshold (default 5%).</param>
    public void CalculateRebalanceNeeded(decimal tolerancePercent = 5m)
    {
        RebalanceDelta = TargetSkew - CurrentSkew;
        RebalanceNeeded = Math.Abs(RebalanceDelta) > tolerancePercent;
    }

    /// <summary>
    /// Creates a copy of this state.
    /// </summary>
    public InventoryState Clone()
    {
        return new InventoryState
        {
            CryptoAllocation = CryptoAllocation,
            UsdtAllocation = UsdtAllocation,
            TargetSkew = TargetSkew,
            CurrentSkew = CurrentSkew,
            RebalanceNeeded = RebalanceNeeded,
            RebalanceDelta = RebalanceDelta,
            TrendState = TrendState,
            LastTrendChange = LastTrendChange
        };
    }
}
