namespace GridBot.Core.Models;

/// <summary>
/// Simple trading state - only 2 states, not 16.
/// </summary>
public enum TradingState
{
    /// <summary>
    /// Grid is actively trading.
    /// </summary>
    Active,

    /// <summary>
    /// Grid is paused (manual pause or risk event).
    /// Cooldown timer may be running.
    /// </summary>
    Paused
}
