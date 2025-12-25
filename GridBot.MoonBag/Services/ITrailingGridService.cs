using GridBot.MoonBag.Models;

namespace GridBot.MoonBag.Services;

/// <summary>
/// Service for managing trailing grid operations during moon bag protection.
/// Handles upward grid shifts when price breaks out above the grid upper bound.
/// </summary>
public interface ITrailingGridService
{
    /// <summary>
    /// Detects if the current price has broken out above the grid upper bound.
    /// </summary>
    Task<bool> DetectBreakoutAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Shifts the grid upward following a price breakout.
    /// Applies cooldown, flash spike protection, and cumulative shift limits.
    /// </summary>
    Task<GridShiftResult> ShiftGridUpwardAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the remaining cooldown time before the next grid shift is allowed.
    /// </summary>
    Task<TimeSpan> GetShiftCooldownRemainingAsync(int marketId);

    /// <summary>
    /// Checks if flash spike protection is currently active for the market.
    /// </summary>
    Task<bool> IsFlashSpikeActiveAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the cumulative grid shift percentage in the last hour.
    /// </summary>
    Task<decimal> GetCumulativeShift1hAsync(int marketId);

    /// <summary>
    /// Records a price point for flash spike detection.
    /// </summary>
    Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Clears flash spike protection for a market (manual override).
    /// </summary>
    void ClearFlashSpikeProtection(int marketId);
}
