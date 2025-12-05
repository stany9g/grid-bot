namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current operational state of the trading bot.
/// CORE PRINCIPLE: THE BOT NEVER HALTS. These states represent operational capacity levels.
/// </summary>
public enum TradingState
{
    /// <summary>
    /// Full operation - 100% capacity, all systems functioning normally.
    /// </summary>
    Active,

    /// <summary>
    /// Starting fresh with no position - buy-only grid to build initial position.
    /// Operates at reduced capacity (50%) with conservative sizes.
    /// </summary>
    Degraded_Bootstrap,

    /// <summary>
    /// Inventory skew outside acceptable range - asymmetric grid favoring correction.
    /// Operates at 75% capacity, biases orders toward correction direction.
    /// </summary>
    Degraded_SkewCorrection,

    /// <summary>
    /// Volatility spike detected - wider spreads, smaller orders.
    /// Operates at reduced capacity based on volatility level.
    /// </summary>
    Degraded_HighVolatility,

    /// <summary>
    /// Order book thin - wider spreads, fewer grid levels.
    /// Operates at reduced capacity to minimize slippage risk.
    /// </summary>
    Degraded_LowLiquidity,

    /// <summary>
    /// Loss approaching limits - protective mode with position reduction.
    /// Minimum 10% capacity, close-only mode, aggressive trailing stops.
    /// </summary>
    Degraded_ProtectiveMode,

    /// <summary>
    /// Returning to normal after a degraded state.
    /// Gradual capacity increase while validating stability.
    /// </summary>
    Recovering
}
