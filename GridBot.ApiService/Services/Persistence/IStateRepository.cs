using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Risk;

namespace GridBot.ApiService.Services.Persistence;

/// <summary>
/// Repository interface for persisting critical trading state to Redis.
/// Enables state recovery across application restarts.
/// Thread-safe for concurrent access.
/// </summary>
public interface IStateRepository
{
    /// <summary>
    /// Saves recovery state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="state">Recovery state to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveRecoveryStateAsync(int marketId, RecoveryState state, CancellationToken ct = default);

    /// <summary>
    /// Loads recovery state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Recovery state if found, null otherwise.</returns>
    Task<RecoveryState?> LoadRecoveryStateAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Deletes recovery state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteRecoveryStateAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Saves moon bag status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="status">Moon bag status to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveMoonBagStatusAsync(int marketId, MoonBagStatus status, CancellationToken ct = default);

    /// <summary>
    /// Loads moon bag status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Moon bag status if found, null otherwise.</returns>
    Task<MoonBagStatus?> LoadMoonBagStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Saves trading state and trend state.
    /// </summary>
    /// <param name="state">Trading state to persist.</param>
    /// <param name="trendState">Trend state to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveTradingStateAsync(TradingState state, TrendState trendState, CancellationToken ct = default);

    /// <summary>
    /// Loads trading state and trend state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of trading state and trend state if found, both null otherwise.</returns>
    Task<(TradingState?, TrendState?)> LoadTradingStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves loss status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="status">Loss status to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveLossStatusAsync(int marketId, LossStatus status, CancellationToken ct = default);

    /// <summary>
    /// Loads loss status for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Loss status if found, null otherwise.</returns>
    Task<LossStatus?> LoadLossStatusAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Records a circuit breaker event with automatic 24h expiration.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="triggerType">Type of circuit breaker trigger.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordCircuitBreakerEventAsync(int marketId, string triggerType, CancellationToken ct = default);

    /// <summary>
    /// Gets the count of circuit breaker events in the last 24 hours.
    /// Used for cascade detection.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Count of circuit breaker events in last 24 hours.</returns>
    Task<int> GetCircuitBreakerCount24hAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Saves a trade record for rolling P&amp;L calculations.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="record">Trade record to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveTradeRecordAsync(int marketId, TradeRecord record, CancellationToken ct = default);

    /// <summary>
    /// Loads trade records for a market since a given timestamp.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="since">Only load records after this timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of trade records.</returns>
    Task<IReadOnlyList<TradeRecord>> LoadTradeRecordsAsync(int marketId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Cleans up trade records older than the retention period.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="retentionDays">Number of days to retain.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CleanupOldTradeRecordsAsync(int marketId, int retentionDays, CancellationToken ct = default);

    /// <summary>
    /// Saves an equity snapshot for validation and fallback calculations.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="snapshot">Equity snapshot to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveEquitySnapshotAsync(int marketId, EquitySnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Loads equity snapshots for a market since a given timestamp.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="since">Only load snapshots after this timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of equity snapshots.</returns>
    Task<IReadOnlyList<EquitySnapshot>> LoadEquitySnapshotsAsync(int marketId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Cleans up equity snapshots older than the retention period.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="retentionDays">Number of days to retain.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CleanupOldEquitySnapshotsAsync(int marketId, int retentionDays, CancellationToken ct = default);

    /// <summary>
    /// Saves rolling loss state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="state">Rolling loss state to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveRollingLossStateAsync(int marketId, PersistedRollingLossState state, CancellationToken ct = default);

    /// <summary>
    /// Loads rolling loss state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rolling loss state if found, null otherwise.</returns>
    Task<PersistedRollingLossState?> LoadRollingLossStateAsync(int marketId, CancellationToken ct = default);
}
