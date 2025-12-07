namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current inventory/exposure state for trend-based position management.
/// For perpetual futures: skew ranges from -100% (fully short) to +100% (fully long).
/// </summary>
public sealed class InventoryState
{
    /// <summary>
    /// Current position exposure percentage (-100 to +100 for perpetuals).
    /// Positive = long exposure, Negative = short exposure, Zero = flat.
    /// </summary>
    public decimal CryptoAllocation { get; set; } = 0m;

    /// <summary>
    /// Collateral/margin percentage. For perpetuals, represents available margin.
    /// </summary>
    public decimal UsdtAllocation { get; set; } = 100m;

    /// <summary>
    /// Target exposure percentage based on current trend state (-100 to +100).
    /// Positive = long target, Negative = short target, Zero = flat.
    /// </summary>
    public decimal TargetSkew { get; set; } = 0m;

    /// <summary>
    /// Current exposure percentage (-100 to +100).
    /// Positive = long, Negative = short, Zero = flat.
    /// </summary>
    public decimal CurrentSkew { get; set; } = 0m;

    /// <summary>
    /// Whether a rebalance is needed based on tolerance threshold.
    /// True if |CurrentSkew - TargetSkew| > tolerance (typically 5%).
    /// </summary>
    public bool RebalanceNeeded { get; set; }

    /// <summary>
    /// The delta needed to reach target skew.
    /// Positive = need to increase exposure (buy/cover), Negative = need to reduce exposure (sell/short).
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
    /// For perpetual futures: negative values indicate short exposure targets.
    /// </summary>
    public static decimal GetTargetSkewForTrend(TrendState trend)
    {
        return trend switch
        {
            TrendState.StrongBull => 80m,    // +80% long exposure
            TrendState.MildBull => 50m,      // +50% long exposure
            TrendState.Neutral => 0m,        // 0% = flat (no position)
            TrendState.MildBear => -50m,     // -50% short exposure
            TrendState.StrongBear => -80m,   // -80% short exposure
            _ => 0m                          // Default to flat
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
