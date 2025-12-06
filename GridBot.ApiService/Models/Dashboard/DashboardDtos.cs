namespace GridBot.ApiService.Models.Dashboard;

/// <summary>
/// Comprehensive dashboard response containing all monitoring data.
/// </summary>
public sealed record DashboardResponse
{
    // Trading State
    public string TradingState { get; init; } = "Unknown";
    public DateTimeOffset StateStartedAt { get; init; }
    public string TrendState { get; init; } = "Neutral";
    public TimeSpan Uptime { get; init; }
    public int MarketId { get; init; }
    public string RecoveryPhase { get; init; } = "None";
    public decimal PositionMultiplier { get; init; } = 1.0m;
    public decimal SpreadMultiplier { get; init; } = 1.0m;
    public int ConsecutiveTimeouts { get; init; }
    public int OperationalCapacity { get; init; } = 100;

    // Market Data
    public decimal CurrentPrice { get; init; }
    public decimal PositionSize { get; init; }
    public decimal Equity { get; init; }
    public decimal AvailableBalance { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal UnrealizedPnlPercent { get; init; }

    // Grid State
    public GridStateDto? Grid { get; init; }

    // Risk Info
    public RiskInfoDto? Risk { get; init; }

    // Trend Info
    public TrendInfoDto? Trend { get; init; }

    // Moon Bag Info
    public MoonBagInfoDto? MoonBag { get; init; }

    // Decision Cycle Info
    public DecisionCycleDto? Cycle { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record GridStateDto
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
    public int RebuildCount { get; init; }
    public int ActiveOrders { get; init; }
    public List<GridLevelDto> Levels { get; init; } = [];
}

public sealed record GridLevelDto
{
    public decimal Price { get; init; }
    public bool IsBid { get; init; }
    public int LevelIndex { get; init; }
    public decimal Size { get; init; }
    public string Status { get; init; } = "Pending";
}

public sealed record RiskInfoDto
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
    public List<string> ActiveWarnings { get; init; } = [];
}

public sealed record TrendInfoDto
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

public sealed record MoonBagInfoDto
{
    public string State { get; init; } = "Inactive";
    public decimal LockedQuantity { get; init; }
    public decimal HighWatermarkPrice { get; init; }
    public decimal TrailingStopPrice { get; init; }
    public decimal CurrentProfitPercent { get; init; }
    public decimal MaxPositionAchieved { get; init; }
    public bool HasActiveStopOrder { get; init; }
}

public sealed record DecisionCycleDto
{
    public DateTimeOffset LastCycleTime { get; init; }
    public int CycleTimeMs { get; init; }
    public int DataCollectionTimeMs { get; init; }
    public int OrdersPlaced { get; init; }
    public int OrdersCancelled { get; init; }
    public bool Success { get; init; }
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Response DTO for trading status endpoint.
/// </summary>
public sealed record TradingStatusResponse(
    string State,
    string TrendState,
    DateTimeOffset StateStartedAt,
    TimeSpan Uptime,
    int MarketId,
    string RecoveryPhase,
    decimal PositionMultiplier,
    decimal SpreadMultiplier,
    int ConsecutiveTimeouts
);

/// <summary>
/// Response DTO for position summary endpoint.
/// </summary>
public sealed record PositionSummaryResponse(
    int MarketId,
    string MoonBagState,
    decimal LockedQuantity,
    decimal HighWatermarkPrice,
    decimal TrailingStopPrice,
    decimal CurrentProfitPercent,
    decimal MaxPositionAchieved,
    bool HasActiveStopOrder
);

/// <summary>
/// Response DTO for risk indicators endpoint.
/// </summary>
public sealed record RiskIndicatorsResponse(
    int MarketId,
    decimal DailyPnlPercent,
    decimal WeeklyPnlPercent,
    decimal MonthlyPnlPercent,
    decimal DrawdownPercent,
    bool AnyLimitBreached,
    string? HaltReason,
    DateTimeOffset? HaltUntil,
    bool FlashCrashActive,
    string FlashCrashSeverity,
    string FlashCrashAction,
    DateTimeOffset? FlashCrashProtectionUntil,
    int CrashCount24h
);

/// <summary>
/// Response DTO for control endpoints (pause, resume, halt).
/// </summary>
public sealed record ControlResponse(bool Success, string Message);
