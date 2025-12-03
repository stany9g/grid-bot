namespace GridBot.ApiService.Services.Persistence;

/// <summary>
/// Provides Redis key generation for state persistence.
/// All keys use the "alte:" prefix for namespace isolation.
/// </summary>
public static class StateKeys
{
    private const string Prefix = "alte";

    /// <summary>
    /// Gets the key for recovery state storage.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Redis key for recovery state.</returns>
    public static string Recovery(int marketId) => $"{Prefix}:recovery:{marketId}";

    /// <summary>
    /// Gets the key for moon bag status storage.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Redis key for moon bag status.</returns>
    public static string MoonBag(int marketId) => $"{Prefix}:moonbag:{marketId}";

    /// <summary>
    /// Gets the key for trading state storage.
    /// </summary>
    /// <returns>Redis key for trading state.</returns>
    public static string TradingState() => $"{Prefix}:tradingstate";

    /// <summary>
    /// Gets the key for loss status storage.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Redis key for loss status.</returns>
    public static string LossStatus(int marketId) => $"{Prefix}:loss:{marketId}";

    /// <summary>
    /// Gets the key for circuit breaker event storage.
    /// Uses sorted set for time-based expiration queries.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Redis key for circuit breaker events.</returns>
    public static string CircuitBreaker(int marketId) => $"{Prefix}:circuitbreaker:{marketId}";

    /// <summary>
    /// Gets the key pattern for all market-specific keys.
    /// Useful for cleanup or enumeration.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Redis key pattern for market keys.</returns>
    public static string MarketPattern(int marketId) => $"{Prefix}:*:{marketId}";
}
