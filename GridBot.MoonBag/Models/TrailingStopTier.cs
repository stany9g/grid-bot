namespace GridBot.MoonBag.Models;

/// <summary>
/// Trailing stop distance tier based on unrealized profit percentage.
/// Tightening is one-way only - once tightened, the stop distance never widens.
/// </summary>
public enum TrailingStopTier
{
    /// <summary>
    /// Standard trailing stop at 15% below high watermark.
    /// Active for 0-50% unrealized profit.
    /// </summary>
    Standard,

    /// <summary>
    /// Tightened trailing stop at 10% below high watermark.
    /// Active for 50-100% unrealized profit.
    /// </summary>
    Tightened,

    /// <summary>
    /// Aggressive trailing stop at 7% below high watermark.
    /// Active for 100-200% unrealized profit.
    /// </summary>
    Aggressive,

    /// <summary>
    /// Emergency trailing stop at 5% below high watermark.
    /// Active for >200% unrealized profit.
    /// </summary>
    Emergency
}
