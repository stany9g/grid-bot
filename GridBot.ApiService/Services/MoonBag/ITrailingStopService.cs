using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.MoonBag;

/// <summary>
/// Service for managing trailing stop functionality during moon bag protection.
/// Handles stop price calculation, tier management, and stop execution.
/// </summary>
public interface ITrailingStopService
{
    /// <summary>
    /// Gets the current trailing stop price and tier for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of (stopPrice, tier), or (0, Standard) if not tracking.</returns>
    Task<(decimal StopPrice, TrailingStopTier Tier)> GetCurrentTrailingStopAsync(
        int marketId,
        CancellationToken ct = default);

    /// <summary>
    /// Calculates the trailing stop price based on high watermark and current profit tier.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Calculated trailing stop price.</returns>
    Task<decimal> CalculateTrailingStopAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Determines the trailing stop tier based on current profit percentage.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current trailing stop tier.</returns>
    Task<TrailingStopTier> GetTrailingStopTierAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if the trailing stop has been triggered.
    /// Requires 3 consecutive confirmations.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if stop is triggered and confirmed, false otherwise.</returns>
    Task<bool> IsTrailingStopTriggeredAsync(
        int marketId,
        decimal currentPrice,
        CancellationToken ct = default);

    /// <summary>
    /// Executes the trailing stop - sells 85% of position, keeps moon bag.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if execution succeeded, false otherwise.</returns>
    Task<bool> ExecuteTrailingStopAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Updates the trailing stop order on Lighter DEX.
    /// Called when high watermark increases or tier changes.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if order was updated, false otherwise.</returns>
    Task<bool> UpdateTrailingStopOrderAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if sufficient time has passed since last trailing stop order update.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>True if update is allowed, false if in cooldown.</returns>
    Task<bool> CanUpdateTrailingStopOrderAsync(int marketId);

    /// <summary>
    /// Cancels the trailing stop order for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if cancelled successfully, false otherwise.</returns>
    Task<bool> CancelTrailingStopOrderAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Resets the trailing stop trigger confirmation counter.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    void ResetTriggerConfirmation(int marketId);
}
