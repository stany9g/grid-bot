namespace GridBot.Core.Models;

/// <summary>
/// Current state of the grid.
/// </summary>
public sealed class GridState
{
    /// <summary>
    /// Current trading state.
    /// </summary>
    public TradingState State { get; set; } = TradingState.Active;

    /// <summary>
    /// Current center price of the grid.
    /// </summary>
    public decimal CenterPrice { get; set; }

    /// <summary>
    /// All grid levels (both buy and sell).
    /// </summary>
    public List<GridLevel> Levels { get; set; } = [];

    /// <summary>
    /// When the grid was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// If paused, when the cooldown expires.
    /// </summary>
    public DateTimeOffset? CooldownUntil { get; set; }

    /// <summary>
    /// Reason for current pause (if paused).
    /// </summary>
    public string? PauseReason { get; set; }

    /// <summary>
    /// Total number of fills today.
    /// </summary>
    public int TodayFillCount { get; set; }

    /// <summary>
    /// Total profit/loss today in USDC.
    /// </summary>
    public decimal TodayPnlUsdc { get; set; }

    /// <summary>
    /// Whether the grid is currently active.
    /// </summary>
    public bool IsActive => State == TradingState.Active;

    /// <summary>
    /// Whether the cooldown period has expired.
    /// </summary>
    public bool IsCooldownExpired => CooldownUntil == null || DateTimeOffset.UtcNow >= CooldownUntil;

    /// <summary>
    /// Gets buy levels (orders below center price).
    /// </summary>
    public IEnumerable<GridLevel> BuyLevels => Levels.Where(l => l.IsBuy);

    /// <summary>
    /// Gets sell levels (orders above center price).
    /// </summary>
    public IEnumerable<GridLevel> SellLevels => Levels.Where(l => !l.IsBuy);

    /// <summary>
    /// Number of active buy orders.
    /// </summary>
    public int ActiveBuyOrderCount => BuyLevels.Count(l => l.HasActiveOrder);

    /// <summary>
    /// Number of active sell orders.
    /// </summary>
    public int ActiveSellOrderCount => SellLevels.Count(l => l.HasActiveOrder);
}
