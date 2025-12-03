namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current P&amp;L and loss limit status.
/// </summary>
public sealed class LossStatus
{
    /// <summary>
    /// Daily P&amp;L as percentage of starting equity.
    /// </summary>
    public decimal DailyPnlPercent { get; init; }

    /// <summary>
    /// Weekly P&amp;L as percentage of starting equity.
    /// </summary>
    public decimal WeeklyPnlPercent { get; init; }

    /// <summary>
    /// Monthly P&amp;L as percentage of starting equity.
    /// </summary>
    public decimal MonthlyPnlPercent { get; init; }

    /// <summary>
    /// Current drawdown from all-time high as percentage.
    /// </summary>
    public decimal DrawdownFromAthPercent { get; init; }

    /// <summary>
    /// All-time high equity value used for drawdown calculation.
    /// </summary>
    public decimal EquityHighWaterMark { get; init; }

    /// <summary>
    /// Current equity value.
    /// </summary>
    public decimal CurrentEquity { get; init; }

    /// <summary>
    /// Whether daily loss limit has been breached.
    /// </summary>
    public bool DailyLimitBreached { get; init; }

    /// <summary>
    /// Whether weekly loss limit has been breached.
    /// </summary>
    public bool WeeklyLimitBreached { get; init; }

    /// <summary>
    /// Whether monthly loss limit has been breached.
    /// </summary>
    public bool MonthlyLimitBreached { get; init; }

    /// <summary>
    /// Whether max drawdown limit has been breached.
    /// </summary>
    public bool DrawdownLimitBreached { get; init; }

    /// <summary>
    /// When trading halt expires, if currently halted due to loss limit.
    /// Null if not halted or requires manual restart.
    /// </summary>
    public DateTimeOffset? HaltUntil { get; init; }

    /// <summary>
    /// Reason for current halt, if any.
    /// </summary>
    public string? HaltReason { get; init; }

    /// <summary>
    /// Whether any loss limit has been breached.
    /// </summary>
    public bool AnyLimitBreached =>
        DailyLimitBreached || WeeklyLimitBreached || MonthlyLimitBreached || DrawdownLimitBreached;

    /// <summary>
    /// Creates a default status with no losses.
    /// </summary>
    public static LossStatus CreateDefault(decimal currentEquity) => new()
    {
        DailyPnlPercent = 0m,
        WeeklyPnlPercent = 0m,
        MonthlyPnlPercent = 0m,
        DrawdownFromAthPercent = 0m,
        EquityHighWaterMark = currentEquity,
        CurrentEquity = currentEquity,
        DailyLimitBreached = false,
        WeeklyLimitBreached = false,
        MonthlyLimitBreached = false,
        DrawdownLimitBreached = false,
        HaltUntil = null,
        HaltReason = null
    };
}
