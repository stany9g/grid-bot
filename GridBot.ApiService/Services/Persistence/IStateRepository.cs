using GridBot.ApiService.Models.Trading;

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
}
