using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Monitors P&amp;L and loss limits for trading protection.
/// Thread-safe for concurrent access.
/// </summary>
public interface ILossMonitor
{
    /// <summary>
    /// Gets the current loss status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current loss status including P&amp;L and limit breach information.</returns>
    Task<LossStatus> GetCurrentLossStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records a trade result for P&amp;L tracking.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="pnlPercent">P&amp;L of the trade as percentage of equity.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default);

    /// <summary>
    /// Records an equity update for drawdown tracking.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentEquity">Current equity value in USD.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordEquityAsync(int marketId, decimal currentEquity, CancellationToken ct = default);

    /// <summary>
    /// Checks all loss limits and returns whether trading should continue.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if trading is allowed, false if a limit has been breached.</returns>
    Task<bool> CheckLossLimitsAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Resets daily loss tracking. Should be called at UTC midnight.
    /// </summary>
    void ResetDailyLimits();

    /// <summary>
    /// Resets weekly loss tracking. Should be called at week start.
    /// </summary>
    void ResetWeeklyLimits();

    /// <summary>
    /// Resets monthly loss tracking. Should be called at month start.
    /// </summary>
    void ResetMonthlyLimits();

    /// <summary>
    /// Manually clears a halt condition. Use with caution.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    void ClearHalt(int marketId);

    /// <summary>
    /// Loads persisted loss status from storage.
    /// Call this on startup to restore state across restarts.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if state was loaded, false if no persisted state exists.</returns>
    Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default);
}
