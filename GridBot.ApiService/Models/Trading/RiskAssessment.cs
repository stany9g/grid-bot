namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Aggregated risk assessment combining all risk monitors.
/// </summary>
public sealed class RiskAssessment
{
    /// <summary>
    /// Market ID for this assessment.
    /// </summary>
    public int MarketId { get; init; }

    /// <summary>
    /// Whether trading is allowed based on all risk factors.
    /// </summary>
    public bool TradingAllowed { get; init; }

    /// <summary>
    /// Whether only buy orders are blocked.
    /// </summary>
    public bool BuysBlocked { get; init; }

    /// <summary>
    /// Whether only sell orders are blocked.
    /// </summary>
    public bool SellsBlocked { get; init; }

    /// <summary>
    /// Current rolling loss status.
    /// </summary>
    public required RollingLossStatus LossStatus { get; init; }

    /// <summary>
    /// Current flash crash status.
    /// </summary>
    public required FlashCrashStatus FlashCrashStatus { get; init; }

    /// <summary>
    /// Current liquidity status.
    /// </summary>
    public required LiquidityStatus LiquidityStatus { get; init; }

    /// <summary>
    /// Overall severity level (highest of all active alerts).
    /// </summary>
    public AlertSeverity OverallSeverity { get; init; }

    /// <summary>
    /// List of active warning messages.
    /// </summary>
    public List<string> ActiveWarnings { get; init; } = [];

    /// <summary>
    /// Recent risk events.
    /// </summary>
    public List<RiskEvent> RecentEvents { get; init; } = [];

    /// <summary>
    /// When this assessment was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Recommended position size multiplier based on risk (0.0 to 1.0).
    /// 1.0 = normal size, 0.25 = 75% reduction, 0.0 = no trading.
    /// </summary>
    public decimal RecommendedPositionMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Recommended spread multiplier based on liquidity (1.0+).
    /// 1.0 = normal spread, 1.25 = 25% wider, 2.0 = 100% wider.
    /// </summary>
    public decimal RecommendedSpreadMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Whether immediate action is required.
    /// </summary>
    public bool RequiresImmediateAction =>
        OverallSeverity == AlertSeverity.Critical ||
        !TradingAllowed ||
        LossStatus.AnyLimitBreached ||
        FlashCrashStatus.CrashDetected;

    /// <summary>
    /// Creates an assessment indicating all systems are normal.
    /// </summary>
    public static RiskAssessment AllClear(int marketId, RollingLossStatus lossStatus, LiquidityStatus liquidityStatus) => new()
    {
        MarketId = marketId,
        TradingAllowed = true,
        BuysBlocked = false,
        SellsBlocked = false,
        LossStatus = lossStatus,
        FlashCrashStatus = FlashCrashStatus.NoCrash(),
        LiquidityStatus = liquidityStatus,
        OverallSeverity = AlertSeverity.Low,
        ActiveWarnings = [],
        RecentEvents = [],
        RecommendedPositionMultiplier = 1.0m,
        RecommendedSpreadMultiplier = liquidityStatus.RecommendedSpreadMultiplier
    };
}
