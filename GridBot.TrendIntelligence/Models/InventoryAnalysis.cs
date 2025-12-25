namespace GridBot.TrendIntelligence.Models;

/// <summary>
/// Direction of rebalancing operation.
/// </summary>
public enum RebalanceDirection
{
    /// <summary>
    /// No rebalancing needed.
    /// </summary>
    None,

    /// <summary>
    /// Need to buy more crypto to increase skew.
    /// </summary>
    BuyCrypto,

    /// <summary>
    /// Need to sell crypto to decrease skew.
    /// </summary>
    SellCrypto
}

/// <summary>
/// Direction of skew correction needed for inventory management.
/// Used when skew is outside acceptable range for current trend.
/// For perpetual futures: handles both long and short positions.
/// </summary>
public enum SkewCorrectionDirection
{
    /// <summary>
    /// No skew correction needed - within acceptable range.
    /// </summary>
    None,

    /// <summary>
    /// Need to increase exposure - buy more (if flat/long) or cover short (if short).
    /// Used when current skew is below the minimum acceptable range.
    /// </summary>
    IncreaseExposure,

    /// <summary>
    /// Need to reduce exposure - sell (if long) or open/add short (if flat/short).
    /// Used when current skew is above the maximum acceptable range.
    /// </summary>
    ReduceExposure
}

/// <summary>
/// Result of inventory analysis containing current exposure and rebalance requirements.
/// For perpetual futures: skew ranges from -100% (fully short) to +100% (fully long).
/// </summary>
public sealed class InventoryAnalysis
{
    /// <summary>
    /// Current position exposure percentage (-100 to +100 for perpetuals).
    /// Positive = long exposure, Negative = short exposure, Zero = flat.
    /// </summary>
    public decimal CryptoAllocation { get; init; }

    /// <summary>
    /// USDT allocation percentage. For perpetuals, this represents collateral.
    /// </summary>
    public decimal UsdtAllocation { get; init; }

    /// <summary>
    /// Current position exposure percentage (-100 to +100).
    /// Positive = long, Negative = short, Zero = flat.
    /// </summary>
    public decimal CurrentSkew { get; init; }

    /// <summary>
    /// Target exposure percentage based on trend state (-100 to +100).
    /// Positive = long target, Negative = short target, Zero = flat.
    /// </summary>
    public decimal TargetSkew { get; init; }

    /// <summary>
    /// Delta between current and target skew.
    /// Positive = need to increase exposure (buy/cover), Negative = need to reduce exposure (sell/short).
    /// </summary>
    public decimal RebalanceDelta { get; init; }

    /// <summary>
    /// Whether rebalancing is needed (delta exceeds threshold).
    /// </summary>
    public bool RebalanceNeeded { get; init; }

    /// <summary>
    /// Whether this is an emergency rebalance (delta exceeds 30%).
    /// </summary>
    public bool IsEmergency { get; init; }

    /// <summary>
    /// Direction of required rebalancing.
    /// </summary>
    public RebalanceDirection Direction { get; init; }

    /// <summary>
    /// Maximum amount that can be rebalanced this hour (respects 10%/hour limit).
    /// </summary>
    public decimal MaxRebalanceAmount { get; init; }

    /// <summary>
    /// Human-readable reason for the inventory analysis result.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Total portfolio value in USD.
    /// </summary>
    public decimal TotalPortfolioValueUsd { get; init; }

    /// <summary>
    /// Position value in USD (signed: positive for long, negative for short).
    /// </summary>
    public decimal CryptoValueUsd { get; init; }

    /// <summary>
    /// Collateral/margin balance in USD.
    /// </summary>
    public decimal UsdtBalance { get; init; }

    /// <summary>
    /// Whether skew correction mode is needed (asymmetric grid).
    /// When true, the grid should bias orders toward the correction direction.
    /// </summary>
    public bool SkewCorrectionMode { get; init; }

    /// <summary>
    /// Direction of skew correction needed.
    /// IncreaseExposure = buy/cover, ReduceExposure = sell/short.
    /// </summary>
    public SkewCorrectionDirection CorrectionDirection { get; init; }

    /// <summary>
    /// Not applicable for perpetual futures. Always false.
    /// For perpetuals, flat position (0) is normal operation, not bootstrap.
    /// </summary>
    public bool IsBootstrapMode { get; init; }

    /// <summary>
    /// Minimum acceptable skew for current trend state.
    /// </summary>
    public decimal AcceptableSkewMin { get; init; }

    /// <summary>
    /// Maximum acceptable skew for current trend state.
    /// </summary>
    public decimal AcceptableSkewMax { get; init; }

    /// <summary>
    /// How far current skew deviates from the target skew (absolute value).
    /// Used for capacity calculation - higher deviation reduces operational capacity.
    /// </summary>
    public decimal SkewDeviation { get; init; }

    /// <summary>
    /// Empty inventory analysis for use when no data is available.
    /// Defaults to neutral/flat position for perpetual futures.
    /// </summary>
    public static InventoryAnalysis Empty { get; } = new()
    {
        CryptoAllocation = 0m,
        UsdtAllocation = 100m,
        CurrentSkew = 0m,
        TargetSkew = 0m,
        Direction = RebalanceDirection.None,
        CorrectionDirection = SkewCorrectionDirection.None,
        AcceptableSkewMin = -20m,
        AcceptableSkewMax = 20m,
        Reason = "No data available"
    };
}
