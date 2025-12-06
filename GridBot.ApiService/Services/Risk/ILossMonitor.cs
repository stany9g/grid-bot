using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Monitors P&amp;L and loss limits for trading protection using rolling windows.
/// Thread-safe for concurrent access.
/// </summary>
public interface ILossMonitor
{
    /// <summary>
    /// Gets the current rolling loss status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current rolling loss status including P&amp;L and limit breach information.</returns>
    Task<RollingLossStatus> GetCurrentLossStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records a trade result for P&amp;L tracking.
    /// Creates a TradeRecord internally for rolling window calculations.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="pnlPercent">P&amp;L of the trade as percentage of equity.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default);

    /// <summary>
    /// Records an explicit trade for rolling P&amp;L tracking.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="trade">The trade record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordTradeAsync(int marketId, TradeRecord trade, CancellationToken ct = default);

    /// <summary>
    /// Records an equity update for drawdown tracking.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="currentEquity">Current equity value in USD.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordEquityAsync(int marketId, decimal currentEquity, CancellationToken ct = default);

    /// <summary>
    /// Records an equity snapshot for validation and fallback calculations.
    /// Called hourly by the system.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordEquitySnapshotAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the rolling P&amp;L for a specific time window.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="window">Time span for the rolling window.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rolling P&amp;L as percentage.</returns>
    Task<decimal> GetRollingPnlAsync(int marketId, TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// Checks all loss limits and returns whether trading should continue.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if trading is allowed, false if a limit has been breached.</returns>
    Task<bool> CheckLossLimitsAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Manually clears a halt condition. Use with caution.
    /// For max drawdown breaches, starts recovery at 50% capacity.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="operatorId">Identifier of operator clearing the halt.</param>
    /// <param name="reason">Reason for manual clear.</param>
    void ClearHalt(int marketId, string? operatorId = null, string? reason = null);

    /// <summary>
    /// Loads persisted loss status from storage.
    /// Call this on startup to restore state across restarts.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if state was loaded, false if no persisted state exists.</returns>
    Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Cleans up trade records older than the retention period.
    /// Should be called daily.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CleanupOldRecordsAsync(int marketId, CancellationToken ct = default);
}
