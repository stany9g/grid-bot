namespace GridBot.ApiService.Models.Dashboard;

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
