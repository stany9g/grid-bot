using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Persistence;

/// <summary>
/// DTO for persisting trading state to Redis.
/// Converts enums to strings for JSON serialization.
/// </summary>
internal sealed record PersistedTradingState
{
    /// <summary>
    /// Trading state as string.
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Trend state as string.
    /// </summary>
    public required string TrendState { get; init; }

    /// <summary>
    /// When the state was persisted.
    /// </summary>
    public DateTimeOffset PersistedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a persisted state from domain models.
    /// </summary>
    public static PersistedTradingState Create(TradingState state, TrendState trendState)
    {
        return new PersistedTradingState
        {
            State = state.ToString(),
            TrendState = trendState.ToString(),
            PersistedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Converts back to domain models.
    /// </summary>
    public (TradingState?, TrendState?) ToDomain()
    {
        if (!Enum.TryParse<TradingState>(State, out var tradingState))
            return (null, null);

        if (!Enum.TryParse<TrendState>(TrendState, out var trendState))
            return (tradingState, null);

        return (tradingState, trendState);
    }
}
