using GridBot.AdvancedRisk.Models;

namespace GridBot.AdvancedRisk.Services;

/// <summary>
/// Provides and persists advanced risk state.
/// Implemented by GridBot.ApiService to store state (Redis, database, etc.).
/// </summary>
public interface IAdvancedRiskStateProvider
{
    /// <summary>
    /// Gets the current recovery status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current recovery status.</returns>
    Task<RecoveryStatus?> GetRecoveryStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Saves the recovery status for a market.
    /// </summary>
    /// <param name="status">Recovery status to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveRecoveryStatusAsync(RecoveryStatus status, CancellationToken ct = default);

    /// <summary>
    /// Gets whether protective mode is currently active for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if protective mode is active.</returns>
    Task<bool> IsProtectiveModeActiveAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Sets the protective mode state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="isActive">Whether protective mode should be active.</param>
    /// <param name="reason">Reason for the state change.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetProtectiveModeAsync(int marketId, bool isActive, string reason, CancellationToken ct = default);
}
