using System.Text.Json.Serialization;

namespace GridBot.Web.Models;

/// <summary>
/// API response from the dashboard endpoint.
/// </summary>
public sealed record DashboardApiResponse
{
    [JsonPropertyName("tradingState")]
    public string TradingState { get; init; } = "Unknown";

    [JsonPropertyName("stateStartedAt")]
    public DateTimeOffset StateStartedAt { get; init; }

    [JsonPropertyName("trendState")]
    public string TrendState { get; init; } = "Neutral";

    [JsonPropertyName("uptime")]
    public TimeSpan Uptime { get; init; }

    [JsonPropertyName("marketId")]
    public int MarketId { get; init; }

    [JsonPropertyName("recoveryPhase")]
    public string RecoveryPhase { get; init; } = "None";

    [JsonPropertyName("positionMultiplier")]
    public decimal PositionMultiplier { get; init; } = 1.0m;

    [JsonPropertyName("spreadMultiplier")]
    public decimal SpreadMultiplier { get; init; } = 1.0m;

    [JsonPropertyName("consecutiveTimeouts")]
    public int ConsecutiveTimeouts { get; init; }

    [JsonPropertyName("operationalCapacity")]
    public int OperationalCapacity { get; init; } = 100;

    [JsonPropertyName("currentPrice")]
    public decimal CurrentPrice { get; init; }

    [JsonPropertyName("positionSize")]
    public decimal PositionSize { get; init; }

    [JsonPropertyName("equity")]
    public decimal Equity { get; init; }

    [JsonPropertyName("availableBalance")]
    public decimal AvailableBalance { get; init; }

    [JsonPropertyName("unrealizedPnl")]
    public decimal UnrealizedPnl { get; init; }

    [JsonPropertyName("unrealizedPnlPercent")]
    public decimal UnrealizedPnlPercent { get; init; }

    [JsonPropertyName("grid")]
    public GridApiDto? Grid { get; init; }

    [JsonPropertyName("risk")]
    public RiskApiDto? Risk { get; init; }

    [JsonPropertyName("trend")]
    public TrendApiDto? Trend { get; init; }

    [JsonPropertyName("moonBag")]
    public MoonBagApiDto? MoonBag { get; init; }

    [JsonPropertyName("cycle")]
    public CycleApiDto? Cycle { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}

public sealed record GridApiDto
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "Uninitialized";
    [JsonPropertyName("centerPrice")]
    public decimal CenterPrice { get; init; }
    [JsonPropertyName("gridSpacing")]
    public decimal GridSpacing { get; init; }
    [JsonPropertyName("totalWidth")]
    public decimal TotalWidth { get; init; }
    [JsonPropertyName("upperBound")]
    public decimal UpperBound { get; init; }
    [JsonPropertyName("lowerBound")]
    public decimal LowerBound { get; init; }
    [JsonPropertyName("ordersPerSide")]
    public int OrdersPerSide { get; init; }
    [JsonPropertyName("totalFills")]
    public int TotalFills { get; init; }
    [JsonPropertyName("shiftCount")]
    public int ShiftCount { get; init; }
    [JsonPropertyName("rebuildCount")]
    public int RebuildCount { get; init; }
    [JsonPropertyName("activeOrders")]
    public int ActiveOrders { get; init; }
    [JsonPropertyName("levels")]
    public List<GridLevelApiDto> Levels { get; init; } = [];
}

public sealed record GridLevelApiDto
{
    [JsonPropertyName("price")]
    public decimal Price { get; init; }
    [JsonPropertyName("isBid")]
    public bool IsBid { get; init; }
    [JsonPropertyName("levelIndex")]
    public int LevelIndex { get; init; }
    [JsonPropertyName("size")]
    public decimal Size { get; init; }
    [JsonPropertyName("status")]
    public string Status { get; init; } = "Pending";
}

public sealed record RiskApiDto
{
    [JsonPropertyName("tradingAllowed")]
    public bool TradingAllowed { get; init; } = true;
    [JsonPropertyName("buysBlocked")]
    public bool BuysBlocked { get; init; }
    [JsonPropertyName("sellsBlocked")]
    public bool SellsBlocked { get; init; }
    [JsonPropertyName("dailyPnlPercent")]
    public decimal DailyPnlPercent { get; init; }
    [JsonPropertyName("weeklyPnlPercent")]
    public decimal WeeklyPnlPercent { get; init; }
    [JsonPropertyName("monthlyPnlPercent")]
    public decimal MonthlyPnlPercent { get; init; }
    [JsonPropertyName("drawdownPercent")]
    public decimal DrawdownPercent { get; init; }
    [JsonPropertyName("anyLimitBreached")]
    public bool AnyLimitBreached { get; init; }
    [JsonPropertyName("haltReason")]
    public string? HaltReason { get; init; }
    [JsonPropertyName("haltUntil")]
    public DateTimeOffset? HaltUntil { get; init; }
    [JsonPropertyName("flashCrashActive")]
    public bool FlashCrashActive { get; init; }
    [JsonPropertyName("flashCrashSeverity")]
    public string FlashCrashSeverity { get; init; } = "None";
    [JsonPropertyName("flashCrashAction")]
    public string FlashCrashAction { get; init; } = "None";
    [JsonPropertyName("flashCrashProtectionUntil")]
    public DateTimeOffset? FlashCrashProtectionUntil { get; init; }
    [JsonPropertyName("crashCount24h")]
    public int CrashCount24h { get; init; }
    [JsonPropertyName("activeWarnings")]
    public List<string> ActiveWarnings { get; init; } = [];
}

public sealed record TrendApiDto
{
    [JsonPropertyName("currentState")]
    public string CurrentState { get; init; } = "Neutral";
    [JsonPropertyName("proposedState")]
    public string ProposedState { get; init; } = "Neutral";
    [JsonPropertyName("confirmationRequired")]
    public bool ConfirmationRequired { get; init; }
    [JsonPropertyName("ema20")]
    public decimal Ema20 { get; init; }
    [JsonPropertyName("ema50")]
    public decimal Ema50 { get; init; }
    [JsonPropertyName("adx")]
    public decimal Adx { get; init; }
    [JsonPropertyName("targetSkew")]
    public decimal TargetSkew { get; init; } = 50m;
    [JsonPropertyName("currentSkew")]
    public decimal CurrentSkew { get; init; } = 50m;
    [JsonPropertyName("rebalanceDelta")]
    public decimal RebalanceDelta { get; init; }
    [JsonPropertyName("rebalanceNeeded")]
    public bool RebalanceNeeded { get; init; }
    [JsonPropertyName("correctionDirection")]
    public string CorrectionDirection { get; init; } = "None";
    [JsonPropertyName("inCooldown")]
    public bool InCooldown { get; init; }
}

public sealed record MoonBagApiDto
{
    [JsonPropertyName("state")]
    public string State { get; init; } = "Inactive";
    [JsonPropertyName("lockedQuantity")]
    public decimal LockedQuantity { get; init; }
    [JsonPropertyName("highWatermarkPrice")]
    public decimal HighWatermarkPrice { get; init; }
    [JsonPropertyName("trailingStopPrice")]
    public decimal TrailingStopPrice { get; init; }
    [JsonPropertyName("currentProfitPercent")]
    public decimal CurrentProfitPercent { get; init; }
    [JsonPropertyName("maxPositionAchieved")]
    public decimal MaxPositionAchieved { get; init; }
    [JsonPropertyName("hasActiveStopOrder")]
    public bool HasActiveStopOrder { get; init; }
}

public sealed record CycleApiDto
{
    [JsonPropertyName("lastCycleTime")]
    public DateTimeOffset LastCycleTime { get; init; }
    [JsonPropertyName("cycleTimeMs")]
    public int CycleTimeMs { get; init; }
    [JsonPropertyName("dataCollectionTimeMs")]
    public int DataCollectionTimeMs { get; init; }
    [JsonPropertyName("ordersPlaced")]
    public int OrdersPlaced { get; init; }
    [JsonPropertyName("ordersCancelled")]
    public int OrdersCancelled { get; init; }
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Aggregated dashboard state containing all monitoring data.
/// </summary>
public sealed record DashboardState
{
    /// <summary>
    /// Current trading state (Active, Degraded_*, Recovering).
    /// </summary>
    public string TradingState { get; init; } = "Active";

    /// <summary>
    /// When the current trading state started.
    /// </summary>
    public DateTimeOffset StateStartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current trend state (StrongBull, MildBull, Neutral, MildBear, StrongBear).
    /// </summary>
    public string TrendState { get; init; } = "Neutral";

    /// <summary>
    /// Bot uptime since last restart.
    /// </summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>
    /// Market ID being traded.
    /// </summary>
    public int MarketId { get; init; } = 1;

    /// <summary>
    /// Current recovery phase (None, Phase1-4).
    /// </summary>
    public string RecoveryPhase { get; init; } = "None";

    /// <summary>
    /// Position size multiplier (0.0 - 1.0).
    /// </summary>
    public decimal PositionMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Spread multiplier (1.0+).
    /// </summary>
    public decimal SpreadMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Number of consecutive API timeouts.
    /// </summary>
    public int ConsecutiveTimeouts { get; init; }

    /// <summary>
    /// Current market price.
    /// </summary>
    public decimal CurrentPrice { get; init; }

    /// <summary>
    /// Current position size in base asset.
    /// </summary>
    public decimal PositionSize { get; init; }

    /// <summary>
    /// Total equity value in USD.
    /// </summary>
    public decimal Equity { get; init; }

    /// <summary>
    /// Unrealized PnL in USD.
    /// </summary>
    public decimal UnrealizedPnl { get; init; }

    /// <summary>
    /// Unrealized PnL as percentage.
    /// </summary>
    public decimal UnrealizedPnlPercent { get; init; }

    /// <summary>
    /// Operational capacity percentage (0-100).
    /// </summary>
    public int OperationalCapacity { get; init; } = 100;

    /// <summary>
    /// Grid state information.
    /// </summary>
    public GridStateInfo? GridState { get; init; }

    /// <summary>
    /// Risk assessment information.
    /// </summary>
    public RiskInfo? RiskInfo { get; init; }

    /// <summary>
    /// Trend intelligence information.
    /// </summary>
    public TrendInfo? TrendInfo { get; init; }

    /// <summary>
    /// Moon bag protection status.
    /// </summary>
    public MoonBagInfo? MoonBagInfo { get; init; }

    /// <summary>
    /// Decision cycle metrics.
    /// </summary>
    public DecisionCycleInfo? CycleInfo { get; init; }

    /// <summary>
    /// Recent alerts.
    /// </summary>
    public List<AlertItem> RecentAlerts { get; init; } = [];

    /// <summary>
    /// When this state snapshot was created.
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether the dashboard data is stale (last update > 30 seconds ago).
    /// </summary>
    public bool IsStale => DateTimeOffset.UtcNow - LastUpdated > TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates an empty dashboard state.
    /// </summary>
    public static DashboardState Empty => new()
    {
        TradingState = "Unknown",
        StateStartedAt = DateTimeOffset.UtcNow,
        TrendState = "Unknown",
        LastUpdated = DateTimeOffset.UtcNow
    };
}

/// <summary>
/// Grid state information for dashboard display.
/// </summary>
public sealed record GridStateInfo
{
    public string Status { get; init; } = "Uninitialized";
    public decimal CenterPrice { get; init; }
    public decimal GridSpacing { get; init; }
    public decimal TotalWidth { get; init; }
    public decimal UpperBound { get; init; }
    public decimal LowerBound { get; init; }
    public int OrdersPerSide { get; init; }
    public int TotalFills { get; init; }
    public int ShiftCount { get; init; }
    public decimal RealizedPnl { get; init; }
    public List<GridLevelInfo> Levels { get; init; } = [];
}

/// <summary>
/// Individual grid level information.
/// </summary>
public sealed record GridLevelInfo
{
    public decimal Price { get; init; }
    public bool IsBid { get; init; }
    public int LevelIndex { get; init; }
    public decimal Size { get; init; }
    public string Status { get; init; } = "Pending";
}

/// <summary>
/// Risk assessment information for dashboard display.
/// </summary>
public sealed record RiskInfo
{
    public bool TradingAllowed { get; init; } = true;
    public bool BuysBlocked { get; init; }
    public bool SellsBlocked { get; init; }
    public decimal DailyPnlPercent { get; init; }
    public decimal WeeklyPnlPercent { get; init; }
    public decimal MonthlyPnlPercent { get; init; }
    public decimal DrawdownPercent { get; init; }
    public bool AnyLimitBreached { get; init; }
    public string? HaltReason { get; init; }
    public DateTimeOffset? HaltUntil { get; init; }
    public bool FlashCrashActive { get; init; }
    public string FlashCrashSeverity { get; init; } = "None";
    public string FlashCrashAction { get; init; } = "None";
    public DateTimeOffset? FlashCrashProtectionUntil { get; init; }
    public int CrashCount24h { get; init; }
    public decimal RecommendedPositionMultiplier { get; init; } = 1.0m;
    public decimal RecommendedSpreadMultiplier { get; init; } = 1.0m;
    public List<string> ActiveWarnings { get; init; } = [];
}

/// <summary>
/// Trend intelligence information for dashboard display.
/// </summary>
public sealed record TrendInfo
{
    public string CurrentState { get; init; } = "Neutral";
    public string ProposedState { get; init; } = "Neutral";
    public bool ConfirmationRequired { get; init; }
    public decimal Ema20 { get; init; }
    public decimal Ema50 { get; init; }
    public decimal Adx { get; init; }
    public decimal TargetSkew { get; init; } = 50m;
    public decimal CurrentSkew { get; init; } = 50m;
    public decimal RebalanceDelta { get; init; }
    public bool RebalanceNeeded { get; init; }
    public string CorrectionDirection { get; init; } = "None";
    public bool InCooldown { get; init; }
}

/// <summary>
/// Moon bag protection status for dashboard display.
/// </summary>
public sealed record MoonBagInfo
{
    public string State { get; init; } = "Inactive";
    public decimal LockedQuantity { get; init; }
    public decimal HighWatermarkPrice { get; init; }
    public decimal TrailingStopPrice { get; init; }
    public decimal CurrentProfitPercent { get; init; }
    public decimal MaxPositionAchieved { get; init; }
    public bool HasActiveStopOrder { get; init; }
    public string CurrentTier { get; init; } = "Standard";
    public string? StateReason { get; init; }
}

/// <summary>
/// Decision cycle metrics for dashboard display.
/// </summary>
public sealed record DecisionCycleInfo
{
    public DateTimeOffset LastCycleTime { get; init; }
    public int CycleTimeMs { get; init; }
    public int CyclesPerMinute { get; init; }
    public int SkippedCycles { get; init; }
    public int TimeoutCount { get; init; }
    public int ErrorCount { get; init; }
    public int OrdersPlaced { get; init; }
    public int OrdersCancelled { get; init; }
    public int DataCollectionTimeMs { get; init; }
}
