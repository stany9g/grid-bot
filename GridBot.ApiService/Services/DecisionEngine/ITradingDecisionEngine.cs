using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.DecisionEngine;

/// <summary>
/// Central orchestrator for all trading decisions.
/// Coordinates risk assessment, trend intelligence, grid operations, and moon bag protection.
/// Thread-safe for concurrent access.
/// </summary>
public interface ITradingDecisionEngine
{
    /// <summary>
    /// Executes one complete decision loop iteration for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Comprehensive result with all actions taken.</returns>
    Task<DecisionResult> ExecuteDecisionCycleAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Initializes all subsystems for a market.
    /// Must be called before first decision cycle.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if initialization succeeded.</returns>
    Task<bool> InitializeAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gracefully shuts down trading for a market.
    /// Cancels pending orders but preserves positions.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ShutdownAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current recovery phase for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Current recovery phase, or None if not in recovery.</returns>
    RecoveryPhase GetCurrentRecoveryPhase(int marketId);

    /// <summary>
    /// Gets the combined position multiplier from all sources.
    /// Includes risk, recovery, and liquidity multipliers.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Position multiplier (0.10 to 1.0).</returns>
    decimal GetEffectivePositionMultiplier(int marketId);

    /// <summary>
    /// Gets the combined spread multiplier from all sources.
    /// Includes risk, recovery, and liquidity multipliers.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Spread multiplier (1.0 to 3.0).</returns>
    decimal GetEffectiveSpreadMultiplier(int marketId);

    /// <summary>
    /// Quick check if an order can be placed for a market.
    /// Considers all blocks, states, and moon bag constraints.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="isBuy">True for buy orders, false for sell orders.</param>
    /// <returns>True if order placement is allowed.</returns>
    bool CanPlaceOrder(int marketId, bool isBuy);

    /// <summary>
    /// Gets the current count of consecutive data collection timeouts.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Number of consecutive timeouts.</returns>
    int GetConsecutiveTimeoutCount(int marketId);

    /// <summary>
    /// Gets the last decision result for a market.
    /// Used for dashboard display and monitoring.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Last decision result, or null if no decision has been made.</returns>
    DecisionResult? GetLastDecisionResult(int marketId);
}
