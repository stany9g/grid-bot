using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Grid;

/// <summary>
/// Service for managing grid orders on the Lighter DEX.
/// Handles order placement, cancellation, and synchronization.
/// </summary>
public interface IGridOrderManager
{
    /// <summary>
    /// Places orders for the specified grid levels.
    /// Uses PostOnly (maker) orders to minimize fees.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="levels">Grid levels to place orders for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the placement operation.</returns>
    Task<GridPlacementResult> PlaceGridOrdersAsync(
        int marketId,
        IReadOnlyList<GridLevel> levels,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels specific orders by their IDs.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="orderIds">Order IDs to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of orders successfully cancelled.</returns>
    Task<int> CancelGridOrdersAsync(
        int marketId,
        IReadOnlyList<long> orderIds,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels all grid orders in a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of orders successfully cancelled.</returns>
    Task<int> CancelAllGridOrdersAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Synchronizes order status by querying the exchange.
    /// Updates grid levels with current status (active, filled, cancelled).
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="levels">Grid levels to sync.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SyncOrderStatusAsync(
        int marketId,
        IReadOnlyList<GridLevel> levels,
        CancellationToken ct = default);

    /// <summary>
    /// Calculates the order size for a grid level based on capital allocation.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="price">Price at the grid level.</param>
    /// <param name="totalLevels">Total number of levels in the grid.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Order size in base asset units.</returns>
    Task<decimal> CalculateOrderSizeAsync(
        int marketId,
        decimal price,
        int totalLevels,
        CancellationToken ct = default);

    /// <summary>
    /// Queries existing orders from the exchange and cancels them all.
    /// Used on startup to ensure clean state before grid initialization.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tuple containing:
    /// - Success: true if cancellation was verified successful, false if any step failed
    /// - CancelledCount: number of orders that were cancelled (0 if none existed or on failure)
    /// </returns>
    Task<(bool Success, int CancelledCount)> CancelExistingOrdersOnStartupAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Atomically resets filled levels to pending state under the order manager's lock.
    /// This ensures thread-safe coordination between GridLifecycleService and GridOrderManager.
    /// </summary>
    /// <param name="levels">Levels to reset (only Filled status levels will be modified).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of levels that were reset.</returns>
    Task<int> ResetFilledLevelsToPendingAsync(IReadOnlyList<GridLevel> levels, CancellationToken ct = default);
}
