namespace GridBot.MoonBag.Models;

/// <summary>
/// Represents the current state of moon bag protection for a market.
/// </summary>
/// <remarks>
/// State machine transitions:
/// - INACTIVE -> WARMING_UP: position opened
/// - WARMING_UP -> TRACKING: warm-up period complete OR early activation (>5% profit)
/// - TRACKING -> TRAILING: price > initial_grid * 1.10
/// - TRAILING -> TRIGGERED: price <= trailing_stop (3 confirmations)
/// - TRIGGERED -> HOLD_MODE: non-moon-bag portion sold
/// - HOLD_MODE -> TRACKING: position increased above threshold
/// - HOLD_MODE -> RELEASED: release conditions met AND operator approved
/// - RELEASED -> INACTIVE: position closed OR new cycle started
/// </remarks>
public enum MoonBagState
{
    /// <summary>
    /// Moon bag protection not engaged.
    /// No position or position too small for moon bag protection.
    /// </summary>
    Inactive,

    /// <summary>
    /// Position opened, waiting for warm-up period to complete.
    /// Normal grid trading continues during this period.
    /// </summary>
    WarmingUp,

    /// <summary>
    /// Active tracking mode - high watermark being updated.
    /// Position is above moon bag threshold and in profit.
    /// </summary>
    Tracking,

    /// <summary>
    /// Trailing stop is active, protection engaged.
    /// Price has moved >10% above initial grid upper bound.
    /// </summary>
    Trailing,

    /// <summary>
    /// Trailing stop has been triggered, executing exit.
    /// Selling non-moon-bag portion (85%) of position.
    /// </summary>
    Triggered,

    /// <summary>
    /// Only moon bag portion remains, no automated trading.
    /// Position is at or below the moon bag threshold.
    /// </summary>
    HoldMode,

    /// <summary>
    /// Moon bag has been released, normal trading resumed.
    /// Occurs only when release conditions met AND operator approved.
    /// </summary>
    Released
}
