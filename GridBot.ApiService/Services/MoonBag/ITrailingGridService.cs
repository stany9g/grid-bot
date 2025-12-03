using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.MoonBag;

/// <summary>
/// Service for managing trailing grid operations during moon bag protection.
/// Handles upward grid shifts when price breaks out above the grid upper bound.
/// </summary>
public interface ITrailingGridService
{
    /// <summary>
    /// Detects if the current price has broken out above the grid upper bound.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if price is above grid upper bound, false otherwise.</returns>
    Task<bool> DetectBreakoutAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Shifts the grid upward following a price breakout.
    /// Applies cooldown, flash spike protection, and cumulative shift limits.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the grid shift operation.</returns>
    Task<GridShiftResult> ShiftGridUpwardAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the remaining cooldown time before the next grid shift is allowed.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Remaining cooldown time, or TimeSpan.Zero if no cooldown active.</returns>
    Task<TimeSpan> GetShiftCooldownRemainingAsync(int marketId);

    /// <summary>
    /// Checks if flash spike protection is currently active for the market.
    /// Flash spike protection pauses grid shifting when price increases >20% in 5 minutes.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if flash spike protection is active, false otherwise.</returns>
    Task<bool> IsFlashSpikeActiveAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the cumulative grid shift percentage in the last hour.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Cumulative shift percentage (0.0 to 1.0).</returns>
    Task<decimal> GetCumulativeShift1hAsync(int marketId);

    /// <summary>
    /// Records a price point for flash spike detection.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="price">Current price.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Clears flash spike protection for a market (manual override).
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    void ClearFlashSpikeProtection(int marketId);
}
