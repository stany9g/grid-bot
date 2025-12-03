using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.DecisionEngine;

/// <summary>
/// Manages recovery state transitions and capacity scaling after halt conditions.
/// Thread-safe for concurrent access.
/// </summary>
public interface IRecoveryManager
{
    /// <summary>
    /// Gets the current recovery state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current recovery state snapshot, or null if not in recovery.</returns>
    Task<RecoveryState?> GetRecoveryStateAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current recovery state for a market synchronously.
    /// Returns a snapshot copy to prevent external mutation.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Current recovery state snapshot, or null if not in recovery.</returns>
    RecoveryState? GetRecoveryState(int marketId);

    /// <summary>
    /// Starts a recovery process for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="triggerType">Type of trigger that caused the halt.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="currentEquity">Current equity value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartRecoveryAsync(
        int marketId,
        string triggerType,
        decimal currentPrice,
        decimal currentEquity,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if the recovery phase can be advanced based on criteria.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="currentEquity">Current equity value.</param>
    /// <param name="bookDepth">Current order book depth in USD.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if phase can be advanced, false otherwise.</returns>
    Task<bool> CheckPhaseAdvancementAsync(
        int marketId,
        decimal currentPrice,
        decimal currentEquity,
        decimal bookDepth,
        CancellationToken ct = default);

    /// <summary>
    /// Advances to the next recovery phase.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="currentEquity">Current equity value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if advanced successfully, false if already at Phase4 or not in recovery.</returns>
    Task<bool> AdvancePhaseAsync(
        int marketId,
        decimal currentPrice,
        decimal currentEquity,
        CancellationToken ct = default);

    /// <summary>
    /// Resets recovery due to a new trigger event.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="newTriggerType">Type of new trigger.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="currentEquity">Current equity value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ResetRecoveryAsync(
        int marketId,
        string newTriggerType,
        decimal currentPrice,
        decimal currentEquity,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the position and spread multipliers for a recovery phase.
    /// </summary>
    /// <param name="phase">Recovery phase.</param>
    /// <returns>Tuple of (positionMultiplier, spreadMultiplier, gridOrderPercent).</returns>
    (decimal PositionMultiplier, decimal SpreadMultiplier, int GridOrderPercent) GetPhaseMultipliers(RecoveryPhase phase);

    /// <summary>
    /// Checks if recovery is complete (Phase4 with sufficient time).
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if recovery is complete and ready to transition to Active.</returns>
    Task<bool> IsRecoveryCompleteAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Completes recovery and clears the recovery state.
    /// Call this when transitioning from Recovering to Active.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CompleteRecoveryAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records an API error during recovery.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordApiErrorAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records a new circuit breaker trigger during recovery.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordCircuitBreakerAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Updates price tracking for volatility calculation.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdatePriceTrackingAsync(int marketId, decimal currentPrice, CancellationToken ct = default);

    /// <summary>
    /// Gets the estimated time remaining until recovery completes.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Estimated time remaining, or null if not in recovery.</returns>
    TimeSpan? GetEstimatedTimeRemaining(int marketId);

    /// <summary>
    /// Loads persisted recovery state from storage.
    /// Call this on startup to restore state across restarts.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if state was loaded, false if no persisted state exists.</returns>
    Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default);
}
