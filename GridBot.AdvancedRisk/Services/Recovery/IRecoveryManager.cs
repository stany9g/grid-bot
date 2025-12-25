using GridBot.AdvancedRisk.Models;

namespace GridBot.AdvancedRisk.Services.Recovery;

/// <summary>
/// Manages multi-phase recovery from protective mode.
/// Provides graduated re-entry to prevent whipsawing.
/// </summary>
public interface IRecoveryManager
{
    /// <summary>
    /// Gets the current recovery status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current recovery status.</returns>
    Task<RecoveryStatus> GetRecoveryStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Initiates recovery from protective mode.
    /// Starts at Phase 1 with minimal exposure.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="reasonForProtectiveMode">What triggered protective mode.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Initial recovery status.</returns>
    Task<RecoveryStatus> BeginRecoveryAsync(int marketId, string reasonForProtectiveMode, CancellationToken ct = default);

    /// <summary>
    /// Updates recovery state, potentially advancing to next phase.
    /// Call periodically to check if phase transition is due.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated recovery status.</returns>
    Task<RecoveryStatus> UpdateRecoveryAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Aborts recovery and returns to protective mode.
    /// Use when market conditions deteriorate during recovery.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="reason">Why recovery was aborted.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AbortRecoveryAsync(int marketId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Completes recovery and returns to normal operation.
    /// Called automatically when Phase 3 completes.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CompleteRecoveryAsync(int marketId, CancellationToken ct = default);
}
