namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current rolling P&amp;L and loss limit status.
/// Uses rolling windows instead of calendar-based periods.
/// </summary>
public sealed class RollingLossStatus
{
    /// <summary>
    /// P&amp;L over the rolling 24-hour window as percentage.
    /// </summary>
    public decimal Rolling24hPnlPercent { get; init; }

    /// <summary>
    /// P&amp;L over the rolling 7-day window as percentage.
    /// </summary>
    public decimal Rolling7dPnlPercent { get; init; }

    /// <summary>
    /// P&amp;L over the rolling 30-day window as percentage.
    /// </summary>
    public decimal Rolling30dPnlPercent { get; init; }

    /// <summary>
    /// Current drawdown from all-time high as percentage.
    /// </summary>
    public decimal DrawdownFromAthPercent { get; init; }

    /// <summary>
    /// All-time high equity value.
    /// </summary>
    public decimal EquityHighWaterMark { get; init; }

    /// <summary>
    /// Current equity value.
    /// </summary>
    public decimal CurrentEquity { get; init; }

    /// <summary>
    /// Number of trades in the 24h window.
    /// </summary>
    public int TradesIn24h { get; init; }

    /// <summary>
    /// Number of trades in the 7d window.
    /// </summary>
    public int TradesIn7d { get; init; }

    /// <summary>
    /// Whether 24h rolling limit has been breached.
    /// </summary>
    public bool Rolling24hBreached { get; init; }

    /// <summary>
    /// Whether 7d rolling limit has been breached.
    /// </summary>
    public bool Rolling7dBreached { get; init; }

    /// <summary>
    /// Whether 30d rolling limit has been breached.
    /// </summary>
    public bool Rolling30dBreached { get; init; }

    /// <summary>
    /// Whether max drawdown limit has been breached.
    /// </summary>
    public bool DrawdownBreached { get; init; }

    /// <summary>
    /// When trading halt expires, if currently halted.
    /// </summary>
    public DateTimeOffset? HaltUntil { get; init; }

    /// <summary>
    /// Reason for current halt, if any.
    /// </summary>
    public string? HaltReason { get; init; }

    /// <summary>
    /// Whether any rolling limit has been breached.
    /// </summary>
    public bool AnyLimitBreached =>
        Rolling24hBreached || Rolling7dBreached || Rolling30dBreached || DrawdownBreached;

    /// <summary>
    /// Creates a default status with no losses.
    /// </summary>
    public static RollingLossStatus CreateDefault(decimal currentEquity) => new()
    {
        Rolling24hPnlPercent = 0m,
        Rolling7dPnlPercent = 0m,
        Rolling30dPnlPercent = 0m,
        DrawdownFromAthPercent = 0m,
        EquityHighWaterMark = currentEquity,
        CurrentEquity = currentEquity,
        TradesIn24h = 0,
        TradesIn7d = 0,
        Rolling24hBreached = false,
        Rolling7dBreached = false,
        Rolling30dBreached = false,
        DrawdownBreached = false,
        HaltUntil = null,
        HaltReason = null
    };

    /// <summary>
    /// Creates a LossStatus instance for backward compatibility with existing callers.
    /// Maps rolling values to the legacy structure.
    /// </summary>
    public LossStatus ToLegacyLossStatus() => new()
    {
        DailyPnlPercent = Rolling24hPnlPercent,
        WeeklyPnlPercent = Rolling7dPnlPercent,
        MonthlyPnlPercent = Rolling30dPnlPercent,
        DrawdownFromAthPercent = DrawdownFromAthPercent,
        EquityHighWaterMark = EquityHighWaterMark,
        CurrentEquity = CurrentEquity,
        DailyLimitBreached = Rolling24hBreached,
        WeeklyLimitBreached = Rolling7dBreached,
        MonthlyLimitBreached = Rolling30dBreached,
        DrawdownLimitBreached = DrawdownBreached,
        HaltUntil = HaltUntil,
        HaltReason = HaltReason
    };
}
