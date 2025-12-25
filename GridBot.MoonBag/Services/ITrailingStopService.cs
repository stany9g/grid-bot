using GridBot.MoonBag.Models;

namespace GridBot.MoonBag.Services;

/// <summary>
/// Service for managing trailing stop functionality during moon bag protection.
/// Handles stop price calculation, tier management, and stop execution.
/// </summary>
public interface ITrailingStopService
{
    /// <summary>
    /// Gets the current trailing stop price and tier for a market.
    /// </summary>
    Task<(decimal StopPrice, TrailingStopTier Tier)> GetCurrentTrailingStopAsync(
        int marketId,
        CancellationToken ct = default);

    /// <summary>
    /// Calculates the trailing stop price based on high watermark and current profit tier.
    /// </summary>
    Task<decimal> CalculateTrailingStopAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Determines the trailing stop tier based on current profit percentage.
    /// </summary>
    Task<TrailingStopTier> GetTrailingStopTierAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if the trailing stop has been triggered.
    /// Requires confirmations per configuration.
    /// </summary>
    Task<bool> IsTrailingStopTriggeredAsync(
        int marketId,
        decimal currentPrice,
        CancellationToken ct = default);

    /// <summary>
    /// Executes the trailing stop - sells position above moon bag.
    /// </summary>
    Task<bool> ExecuteTrailingStopAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Updates the trailing stop order on the exchange.
    /// </summary>
    Task<bool> UpdateTrailingStopOrderAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if sufficient time has passed since last trailing stop order update.
    /// </summary>
    Task<bool> CanUpdateTrailingStopOrderAsync(int marketId);

    /// <summary>
    /// Cancels the trailing stop order for a market.
    /// </summary>
    Task<bool> CancelTrailingStopOrderAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Resets the trailing stop trigger confirmation counter.
    /// </summary>
    void ResetTriggerConfirmation(int marketId);
}
