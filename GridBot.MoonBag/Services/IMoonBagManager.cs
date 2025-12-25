using GridBot.MoonBag.Models;

namespace GridBot.MoonBag.Services;

/// <summary>
/// Manages moon bag position protection and state machine transitions.
/// Protects a configured percentage of the position from automated selling.
/// </summary>
public interface IMoonBagManager
{
    /// <summary>
    /// Gets the current moon bag status for a market.
    /// </summary>
    Task<MoonBagStatus> GetMoonBagStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Initializes moon bag tracking when a new position is opened.
    /// </summary>
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
    Task<bool> UpdateHighWatermarkAsync(int marketId, decimal price, CancellationToken ct = default);

    /// <summary>
    /// Calculates the moon bag threshold (locked quantity) based on max position achieved.
    /// </summary>
    Task<decimal> CalculateMoonBagThresholdAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if the current position is at or below the moon bag threshold.
    /// </summary>
    Task<bool> IsPositionAtMoonBagLevelAsync(int marketId, decimal currentPositionSize, CancellationToken ct = default);

    /// <summary>
    /// Checks if a sell order should be blocked due to moon bag protection.
    /// </summary>
    Task<bool> ShouldBlockSellOrderAsync(
        int marketId,
        decimal sellQuantity,
        decimal currentPositionSize,
        CancellationToken ct = default);

    /// <summary>
    /// Transitions the moon bag state machine to a new state.
    /// </summary>
    Task<MoonBagStatus> TransitionStateAsync(
        int marketId,
        MoonBagState newState,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if release conditions are met for the moon bag.
    /// </summary>
    Task<bool> CheckReleaseConditionsAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Approves the release of the moon bag (operator action).
    /// </summary>
    Task<MoonBagStatus> ApproveReleaseAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Updates the max position achieved if the current position is larger.
    /// </summary>
    Task<bool> UpdateMaxPositionAsync(int marketId, decimal currentPositionSize, bool? isLong = null, CancellationToken ct = default);

    /// <summary>
    /// Updates profit percentage based on current price and entry price.
    /// </summary>
    Task<decimal> UpdateProfitPercentAsync(int marketId, decimal currentPrice, CancellationToken ct = default);

    /// <summary>
    /// Resets moon bag tracking for a market.
    /// </summary>
    Task ResetMoonBagAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Loads persisted moon bag state from storage.
    /// </summary>
    Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Checks if automatic release conditions are met and performs auto-release if so.
    /// </summary>
    Task<bool> CheckAndPerformAutoReleaseAsync(
        int marketId,
        decimal currentPrice,
        MoonBagTrendState currentTrend,
        CancellationToken ct = default);

    /// <summary>
    /// Allows operator to disable auto-release for a specific market.
    /// </summary>
    Task SetOperatorAutoReleaseOverrideAsync(int marketId, bool disabled, CancellationToken ct = default);

    /// <summary>
    /// Gets the duration the current trend has been StrongBear.
    /// </summary>
    TimeSpan? GetStrongBearDuration(int marketId);
}
