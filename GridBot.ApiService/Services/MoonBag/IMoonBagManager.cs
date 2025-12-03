using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.MoonBag;

/// <summary>
/// Manages moon bag position protection and state machine transitions.
/// Protects a configured percentage of the position from automated selling.
/// </summary>
public interface IMoonBagManager
{
    /// <summary>
    /// Gets the current moon bag status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current moon bag status.</returns>
    Task<MoonBagStatus> GetMoonBagStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Initializes moon bag tracking when a new position is opened.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="positionSize">Initial position size.</param>
    /// <param name="entryPrice">Position entry price.</param>
    /// <param name="gridUpperBound">Initial grid upper bound.</param>
    /// <param name="isLong">Whether the position is long (true) or short (false).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Initialized moon bag status.</returns>
    Task<MoonBagStatus> InitializeMoonBagAsync(
        int marketId,
        decimal positionSize,
        decimal entryPrice,
        decimal gridUpperBound,
        bool isLong = true,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the high watermark price if the new price is higher.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="price">Current price.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if watermark was updated, false otherwise.</returns>
    Task<bool> UpdateHighWatermarkAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Calculates the moon bag threshold (locked quantity) based on max position achieved.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Moon bag locked quantity.</returns>
    Task<decimal> CalculateMoonBagThresholdAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if the current position is at or below the moon bag threshold.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentPositionSize">Current position size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if position is at moon bag level, false otherwise.</returns>
    Task<bool> IsPositionAtMoonBagLevelAsync(int marketId, decimal currentPositionSize, CancellationToken ct = default);

    /// <summary>
    /// Checks if a sell order should be blocked due to moon bag protection.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="sellQuantity">Quantity to sell.</param>
    /// <param name="currentPositionSize">Current position size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if sell should be blocked, false otherwise.</returns>
    Task<bool> ShouldBlockSellOrderAsync(
        int marketId,
        decimal sellQuantity,
        decimal currentPositionSize,
        CancellationToken ct = default);

    /// <summary>
    /// Transitions the moon bag state machine to a new state.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="newState">New state to transition to.</param>
    /// <param name="reason">Reason for the transition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated moon bag status.</returns>
    Task<MoonBagStatus> TransitionStateAsync(
        int marketId,
        MoonBagState newState,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if release conditions are met for the moon bag.
    /// Requires STRONG_BEAR trend and price below 200 MA.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if release conditions are met, false otherwise.</returns>
    Task<bool> CheckReleaseConditionsAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Approves the release of the moon bag (operator action).
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated moon bag status.</returns>
    Task<MoonBagStatus> ApproveReleaseAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Updates the max position achieved if the current position is larger.
    /// Validates position direction - if direction changes, resets moon bag state.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentPositionSize">Current position size (absolute value).</param>
    /// <param name="isLong">Position direction. If null, direction check is skipped.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if max position was updated, false otherwise.</returns>
    Task<bool> UpdateMaxPositionAsync(int marketId, decimal currentPositionSize, bool? isLong = null, CancellationToken ct = default);

    /// <summary>
    /// Updates profit percentage based on current price and entry price.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current profit percentage.</returns>
    Task<decimal> UpdateProfitPercentAsync(int marketId, decimal currentPrice, CancellationToken ct = default);

    /// <summary>
    /// Resets moon bag tracking for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResetMoonBagAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Loads persisted moon bag state from storage.
    /// Call this on startup to restore state across restarts.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if state was loaded, false if no persisted state exists.</returns>
    Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default);
}
