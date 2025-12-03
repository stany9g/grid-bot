namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current operational state of the trading bot.
/// </summary>
public enum TradingState
{
    /// <summary>
    /// Normal trading operations - all systems functioning.
    /// </summary>
    Active,

    /// <summary>
    /// Temporarily stopped - manual pause or non-critical trigger.
    /// Can transition directly back to Active.
    /// </summary>
    Paused,

    /// <summary>
    /// Stopped due to risk trigger (loss limit, flash crash, etc.).
    /// Requires recovery procedure before resuming.
    /// </summary>
    Halted,

    /// <summary>
    /// Gradual re-entry after halt condition.
    /// System operates at reduced capacity while validating stability.
    /// </summary>
    Recovering
}
