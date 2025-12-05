namespace GridBot.ApiService.Models.Trading;

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
/// </summary>
public enum SkewCorrectionDirection
{
    /// <summary>
    /// No skew correction needed - within acceptable range.
    /// </summary>
    None,

    /// <summary>
    /// Need more crypto - buy more aggressively, reduce sells.
    /// </summary>
    NeedMoreCrypto,

    /// <summary>
    /// Need less crypto - sell more aggressively, reduce buys.
    /// </summary>
    NeedLessCrypto
}

/// <summary>
/// Result of inventory analysis containing current allocations and rebalance requirements.
/// </summary>
public sealed class InventoryAnalysis
{
    /// <summary>
    /// Current crypto allocation percentage (0-100).
    /// </summary>
    public decimal CryptoAllocation { get; init; }

    /// <summary>
    /// Current USDT allocation percentage (0-100).
    /// </summary>
    public decimal UsdtAllocation { get; init; }

    /// <summary>
    /// Current crypto skew percentage (same as CryptoAllocation).
    /// </summary>
    public decimal CurrentSkew { get; init; }

    /// <summary>
    /// Target crypto skew percentage based on trend state.
    /// </summary>
    public decimal TargetSkew { get; init; }

    /// <summary>
    /// Delta between current and target skew.
    /// Positive = need more crypto, Negative = need less crypto.
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
    /// Total crypto value in USD.
    /// </summary>
    public decimal CryptoValueUsd { get; init; }

    /// <summary>
    /// USDT balance.
    /// </summary>
    public decimal UsdtBalance { get; init; }

    /// <summary>
    /// Whether skew correction mode is needed (asymmetric grid).
    /// When true, the grid should bias orders toward the correction direction.
    /// </summary>
    public bool SkewCorrectionMode { get; init; }

    /// <summary>
    /// Direction of skew correction needed.
    /// </summary>
    public SkewCorrectionDirection CorrectionDirection { get; init; }

    /// <summary>
    /// Whether this is a bootstrap scenario (position = 0).
    /// In bootstrap mode, only buy orders should be placed.
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
    /// </summary>
    public static InventoryAnalysis Empty { get; } = new()
    {
        CryptoAllocation = 50m,
        UsdtAllocation = 50m,
        CurrentSkew = 50m,
        TargetSkew = 50m,
        Direction = RebalanceDirection.None,
        CorrectionDirection = SkewCorrectionDirection.None,
        AcceptableSkewMin = 35m,
        AcceptableSkewMax = 65m,
        Reason = "No data available"
    };
}
